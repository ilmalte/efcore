// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;

namespace Microsoft.EntityFrameworkCore.Storage;

/// <summary>
///     <para>
///         The base class for non-relational type mapping. Non-relational providers
///         should derive from this class and override <see cref="O:TypeMappingSourceBase.FindMapping" />
///     </para>
///     <para>
///         This type is typically used by database providers (and other extensions). It is generally
///         not used in application code.
///     </para>
/// </summary>
/// <remarks>
///     <para>
///         The service lifetime is <see cref="ServiceLifetime.Singleton" />. This means a single instance
///         is used by many <see cref="DbContext" /> instances. The implementation must be thread-safe.
///         This service cannot depend on services registered as <see cref="ServiceLifetime.Scoped" />.
///     </para>
///     <para>
///         See <see href="https://aka.ms/efcore-docs-providers">Implementation of database providers and extensions</see>
///         for more information and examples.
///     </para>
/// </remarks>
public abstract class TypeMappingSource : TypeMappingSourceBase
{
    private readonly ConcurrentDictionary<(TypeMappingInfo, Type?, ValueConverter?), CoreTypeMapping?> _explicitMappings = new();

    /// <summary>
    ///     Initializes a new instance of this class.
    /// </summary>
    /// <param name="dependencies">Parameter object containing dependencies for this service.</param>
    protected TypeMappingSource(TypeMappingSourceDependencies dependencies)
        : base(dependencies)
    {
    }

    private CoreTypeMapping? FindMappingWithConversion(
        in TypeMappingInfo mappingInfo,
        IReadOnlyList<IProperty>? principals)
    {
        Type? providerClrType = null;
        ValueConverter? customConverter = null;
        if (principals != null)
        {
            for (var i = 0; i < principals.Count; i++)
            {
                var principal = principals[i];
                if (providerClrType == null)
                {
                    var providerType = principal.GetProviderClrType();
                    if (providerType != null)
                    {
                        providerClrType = providerType.UnwrapNullableType();
                    }
                }

                if (customConverter == null)
                {
                    var converter = principal.GetValueConverter();
                    if (converter != null)
                    {
                        customConverter = converter;
                    }
                }
            }
        }

        var resolvedMapping = FindMappingWithConversion(mappingInfo, providerClrType, customConverter);

        ValidateMapping(resolvedMapping, principals?[0]);

        return resolvedMapping;
    }

    private CoreTypeMapping? FindMappingWithConversion(
        TypeMappingInfo mappingInfo,
        Type? providerClrType,
        ValueConverter? customConverter)
        => _explicitMappings.GetOrAdd(
            (mappingInfo, providerClrType, customConverter),
            static (k, self) =>
            {
                var (info, providerType, converter) = k;
                var mapping = providerType == null
                    || providerType == info.ClrType
                        ? self.FindMapping(info)
                        : null;

                if (mapping == null)
                {
                    var sourceType = info.ClrType;
                    if (sourceType != null)
                    {
                        foreach (var converterInfo in self.Dependencies
                                     .ValueConverterSelector
                                     .Select(sourceType, providerType))
                        {
                            var mappingInfoUsed = info.WithConverter(converterInfo);
                            mapping = self.FindMapping(mappingInfoUsed);

                            if (mapping == null
                                && providerType != null)
                            {
                                foreach (var secondConverterInfo in self.Dependencies
                                             .ValueConverterSelector
                                             .Select(providerType))
                                {
                                    mapping = self.FindMapping(mappingInfoUsed.WithConverter(secondConverterInfo));

                                    if (mapping != null)
                                    {
                                        mapping = mapping.Clone(
                                            secondConverterInfo.Create(),
                                            info.ElementTypeMapping,
                                            jsonValueReaderWriter: mappingInfoUsed.JsonValueReaderWriter);
                                        break;
                                    }
                                }
                            }

                            if (mapping != null)
                            {
                                mapping = mapping.Clone(
                                    converterInfo.Create(),
                                    info.ElementTypeMapping,
                                    jsonValueReaderWriter: info.JsonValueReaderWriter);
                                break;
                            }
                        }

                        if (mapping == null)
                        {
                            mapping = self.TryFindCollectionMapping(info, sourceType, providerType);
                        }
                    }
                }

                if (mapping != null
                    && converter != null)
                {
                    mapping = mapping.Clone(
                        converter,
                        info.ElementTypeMapping,
                        jsonValueReaderWriter: info.JsonValueReaderWriter);
                }

                return mapping;
            },
            this);

    /// <summary>
    ///     Attempts to find a type mapping for a collection of primitive types.
    /// </summary>
    /// <param name="info">The mapping info being used.</param>
    /// <param name="modelType">The model type.</param>
    /// <param name="providerType">The provider type.</param>
    /// <returns>The type mapping, or <see langword="null"/> if none was found.</returns>
    protected virtual CoreTypeMapping? TryFindCollectionMapping(
        TypeMappingInfo info,
        Type modelType,
        Type? providerType)
        => TryFindJsonCollectionMapping(
            info, modelType, providerType, out var elementMapping,
            out var collectionReaderWriter)
            ? FindMapping(
                    info.WithConverter(
                        // Note that the converter info is only used temporarily here and never creates an instance.
                        new ValueConverterInfo(modelType, typeof(string), _ => null!)))!
                .Clone(
                    (ValueConverter)Activator.CreateInstance(
                        typeof(CollectionToJsonStringConverter<>).MakeGenericType(
                            modelType.TryGetElementType(typeof(IEnumerable<>))!),
                        collectionReaderWriter!)!,
                    elementMapping,
                    collectionReaderWriter)
            : null;

    /// <summary>
    ///     Finds the type mapping for a given <see cref="IProperty" />.
    /// </summary>
    /// <remarks>
    ///     Note: providers should typically not need to override this method.
    /// </remarks>
    /// <param name="property">The property.</param>
    /// <returns>The type mapping, or <see langword="null" /> if none was found.</returns>
    public override CoreTypeMapping? FindMapping(IProperty property)
    {
        var principals = property.GetPrincipals();
        return FindMappingWithConversion(new TypeMappingInfo(principals), principals);
    }

    /// <summary>
    ///     Finds the type mapping for a given <see cref="Type" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Note: Only call this method if there is no <see cref="IProperty" />
    ///         or <see cref="IModel" /> available, otherwise call <see cref="FindMapping(IProperty)" />
    ///         or <see cref="FindMapping(Type, IModel, CoreTypeMapping)" />
    ///     </para>
    ///     <para>
    ///         Note: providers should typically not need to override this method.
    ///     </para>
    /// </remarks>
    /// <param name="type">The CLR type.</param>
    /// <returns>The type mapping, or <see langword="null" /> if none was found.</returns>
    public override CoreTypeMapping? FindMapping(Type type)
        => FindMappingWithConversion(new TypeMappingInfo(type), null);

    /// <summary>
    ///     Finds the type mapping for a given <see cref="Type" />, taking pre-convention configuration into the account.
    /// </summary>
    /// <remarks>
    ///     Note: Only call this method if there is no <see cref="IProperty" />,
    ///     otherwise call <see cref="FindMapping(IProperty)" />.
    /// </remarks>
    /// <param name="type">The CLR type.</param>
    /// <param name="model">The model.</param>
    /// <param name="elementMapping">The element mapping to use, if known.</param>
    /// <returns>The type mapping, or <see langword="null" /> if none was found.</returns>
    public override CoreTypeMapping? FindMapping(Type type, IModel model, CoreTypeMapping? elementMapping = null)
    {
        type = type.UnwrapNullableType();
        var typeConfiguration = model.FindTypeMappingConfiguration(type);
        TypeMappingInfo mappingInfo;
        Type? providerClrType = null;
        ValueConverter? customConverter = null;
        if (typeConfiguration == null)
        {
            mappingInfo = new TypeMappingInfo(type);
        }
        else
        {
            providerClrType = typeConfiguration.GetProviderClrType()?.UnwrapNullableType();
            customConverter = typeConfiguration.GetValueConverter();
            mappingInfo = new TypeMappingInfo(
                customConverter?.ProviderClrType ?? type,
                unicode: typeConfiguration.IsUnicode(),
                size: typeConfiguration.GetMaxLength(),
                precision: typeConfiguration.GetPrecision(),
                scale: typeConfiguration.GetScale());
        }

        if (elementMapping != null)
        {
            mappingInfo = mappingInfo.WithElementTypeMapping(elementMapping);
        }

        return FindMappingWithConversion(mappingInfo, providerClrType, customConverter);
    }

    /// <summary>
    ///     Finds the type mapping for a given <see cref="MemberInfo" /> representing
    ///     a field or a property of a CLR type.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Note: Only call this method if there is no <see cref="IProperty" /> available, otherwise
    ///         call <see cref="FindMapping(IProperty)" />
    ///     </para>
    ///     <para>
    ///         Note: providers should typically not need to override this method.
    ///     </para>
    /// </remarks>
    /// <param name="member">The field or property.</param>
    /// <returns>The type mapping, or <see langword="null" /> if none was found.</returns>
    public override CoreTypeMapping? FindMapping(MemberInfo member)
        => FindMappingWithConversion(new TypeMappingInfo(member), null);
}
