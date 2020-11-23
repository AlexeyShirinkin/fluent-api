﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace ObjectPrinting
{
    public class PrintingConfig<TOwner>
    {
        private readonly Dictionary<Type, CultureInfo> cultureTypes =
            new Dictionary<Type, CultureInfo>();

        private readonly HashSet<MemberInfo> excludingMembers = new HashSet<MemberInfo>();
        private readonly HashSet<Type> excludingTypes = new HashSet<Type>();

        private readonly HashSet<Type> finalTypes = new HashSet<Type>
        {
            typeof(string), typeof(DateTime), typeof(TimeSpan), typeof(Guid), typeof(DateTimeOffset)
        };

        private readonly Dictionary<MemberInfo, Delegate> printingFunctionsForMembers =
            new Dictionary<MemberInfo, Delegate>();

        private readonly Dictionary<Type, Delegate> printingFunctionsForTypes =
            new Dictionary<Type, Delegate>();

        private readonly HashSet<object> visitedObjects = new HashSet<object>();

        private string indentation = "\t";
        private string separatorBetweenNameAndValue = "=";

        public PrintingConfig<TOwner> AddCultureForType<TPropType>(CultureInfo culture)
        {
            if (typeof(IFormattable).IsAssignableFrom(typeof(TPropType)))
                cultureTypes[typeof(TPropType)] = culture;
            return this;
        }

        public IPropertyPrintingConfig<TOwner, TPropType> Printing<TPropType>()
        {
            return new PropertyPrintingConfig<TOwner, TPropType>(this,
                func => printingFunctionsForTypes[typeof(TPropType)] = func);
        }

        public IPropertyPrintingConfig<TOwner, TPropType> Printing<TPropType>(
            Expression<Func<TOwner, TPropType>> memberSelector)
        {
            if (!(memberSelector.Body is MemberExpression))
                throw new InvalidCastException("unable to cast to MemberExpression");
            var member = ((MemberExpression) memberSelector.Body).Member;
            return new PropertyPrintingConfig<TOwner, TPropType>(this,
                func => printingFunctionsForMembers[member] = func);
        }

        public PrintingConfig<TOwner> Excluding<TPropType>(Expression<Func<TOwner, TPropType>> memberSelector)
        {
            if (!(memberSelector.Body is MemberExpression))
                throw new InvalidCastException("unable to cast to MemberExpression");
            excludingMembers.Add(((MemberExpression) memberSelector.Body).Member);
            return this;
        }

        public PrintingConfig<TOwner> Excluding<TPropType>()
        {
            excludingTypes.Add(typeof(TPropType));
            return this;
        }

        public PrintingConfig<TOwner> ChangeIndentation(string newIndentation)
        {
            indentation = newIndentation;
            return this;
        }

        public PrintingConfig<TOwner> ChangeSeparatorBetweenNameAndValue(string separator)
        {
            separatorBetweenNameAndValue = separator;
            return this;
        }

        public string PrintToString(TOwner obj)
        {
            return PrintToString(obj, 0);
        }

        private string PrintToString(object obj, int nestingLevel)
        {
            if (obj == null)
                return "null" + Environment.NewLine;

            var type = obj.GetType();
            if (type.IsPrimitive || finalTypes.Contains(type))
                return GetSerializedObject(obj);
            if (visitedObjects.Contains(obj))
                return "cycle" + Environment.NewLine;
            if (type.IsClass)
                visitedObjects.Add(obj);

            var currentIndentation = string.Concat(Enumerable.Repeat(indentation, nestingLevel + 1));

            return typeof(ICollection).IsAssignableFrom(type)
                ? GetSerializedCollection(obj, nestingLevel, currentIndentation)
                : GetSerializedMembers(obj, nestingLevel, currentIndentation);
        }

        private string GetSerializedMembers(object obj, int nestingLevel, string currentIndentation)
        {
            var resultString = new StringBuilder().AppendLine(obj.GetType().Name);

            foreach (var member in obj.GetType().GetProperties().Cast<MemberInfo>()
                .Concat(obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                .Where(x => !excludingMembers.Contains(x)
                            && (x is PropertyInfo propertyInfo &&
                                !excludingTypes.Contains(propertyInfo.PropertyType)
                                || x is FieldInfo fieldInfo && !excludingTypes.Contains(fieldInfo.FieldType))))
                resultString.Append(currentIndentation + member.Name + $" {separatorBetweenNameAndValue} " +
                                    GetSerializedMember(member, obj, nestingLevel));

            return resultString.ToString();
        }

        private string GetSerializedCollection(object obj, int nestingLevel, string currentIndentation)
        {
            var resultString = new StringBuilder().AppendLine(obj.GetType().Name);
            foreach (var e in (IEnumerable) obj)
                resultString.Append(currentIndentation + PrintToString(e, nestingLevel + 1));
            return resultString.ToString();
        }

        private string GetSerializedObject(object obj)
        {
            return cultureTypes.ContainsKey(obj.GetType())
                ? string.Format(cultureTypes[obj.GetType()], "{0}" + Environment.NewLine, obj)
                : obj + Environment.NewLine;
        }

        private string GetSerializedMember(MemberInfo member, object obj, int nestingLevel)
        {
            var memberValue = member is PropertyInfo info
                ? info.GetValue(obj)
                : ((FieldInfo) member).GetValue(obj);
            var memberType = member is PropertyInfo propertyInfo
                ? propertyInfo.PropertyType
                : ((FieldInfo) member).FieldType;

            if (printingFunctionsForMembers.ContainsKey(member))
                return printingFunctionsForMembers[member].DynamicInvoke(memberValue)?.ToString();
            if (printingFunctionsForTypes.ContainsKey(memberType))
                return printingFunctionsForTypes[memberType].DynamicInvoke(memberValue)?.ToString();
            return PrintToString(memberValue, nestingLevel + 1);
        }
    }
}