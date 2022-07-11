using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using AutoMapper.Internal;
namespace AutoMapper.Execution
{
    using static Expression;
    using static ExpressionBuilder;
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class ObjectFactory
    {
        private static readonly LockingConcurrentDictionary<Type, Func<object>> CtorCache = new(GenerateConstructor);
        public static object CreateInstance(Type type) => CtorCache.GetOrAdd(type)();
        private static Func<object> GenerateConstructor(Type type) =>
            Lambda<Func<object>>(GenerateConstructorExpression(type).ToObject()).Compile();
        public static object CreateInterfaceProxy(Type interfaceType) => CreateInstance(ProxyGenerator.GetProxyType(interfaceType));
        public static Expression GenerateConstructorExpression(Type type) => type switch
        {
            { IsValueType: true } => Default(type),
            Type stringType when stringType == typeof(string) => Constant(string.Empty),
            { IsInterface: true } => CreateInterfaceExpression(type),
            { IsAbstract: true } => InvalidType(type, $"Cannot create an instance of abstract type {type}."),
            _ => CallConstructor(type)
        };
        private static Expression CallConstructor(Type type)
        {
            var defaultCtor = type.GetConstructor(TypeExtensions.InstanceFlags, null, Type.EmptyTypes, null);
            if (defaultCtor != null)
            {
                return New(defaultCtor);
            }
            var ctorWithOptionalArgs =
                (from ctor in type.GetDeclaredConstructors() let args = ctor.GetParameters() where args.All(p => p.IsOptional) select (ctor, args)).FirstOrDefault();
            if (ctorWithOptionalArgs.args == null)
            {
                return InvalidType(type, $"{type} needs to have a constructor with 0 args or only optional args. Validate your configuration for details.");
            }
            var arguments = ctorWithOptionalArgs.args.Select(p => p.GetDefaultValue());
            return New(ctorWithOptionalArgs.ctor, arguments);
        }
        private static Expression CreateInterfaceExpression(Type type) =>
            type.IsGenericType(typeof(IDictionary<,>)) ? CreateCollection(type, typeof(Dictionary<,>)) : 
            type.IsGenericType(typeof(IReadOnlyDictionary<,>)) ? CreateReadOnlyDictionary(type.GenericTypeArguments) : 
            type.IsGenericType(typeof(ISet<>)) ? CreateCollection(type, typeof(HashSet<>)) : 
            type.IsCollection() ? CreateCollection(type, typeof(List<>), GetIEnumerableArguments(type)) :
            InvalidType(type, $"Cannot create an instance of interface type {type}.");
        private static Type[] GetIEnumerableArguments(Type type) => type.GetIEnumerableType()?.GenericTypeArguments ?? new[] { typeof(object) };
        private static Expression CreateCollection(Type type, Type collectionType, Type[] genericArguments = null) => 
            ToType(New(collectionType.MakeGenericType(genericArguments ?? type.GenericTypeArguments)), type);
        private static Expression CreateReadOnlyDictionary(Type[] typeArguments)
        {
            var ctor = typeof(ReadOnlyDictionary<,>).MakeGenericType(typeArguments).GetConstructors()[0];
            return New(ctor, New(typeof(Dictionary<,>).MakeGenericType(typeArguments)));
        }
        private static Expression InvalidType(Type type, string message) => Throw(Constant(new ArgumentException(message, "type")), type);
    }
}