// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.Storage.Json;

namespace Microsoft.EntityFrameworkCore.Storage;

/// <summary>
///     Describes metadata needed to decide on a type mapping for a property or type.
/// </summary>
/// <remarks>
///     See <see href="https://aka.ms/efcore-docs-providers">Implementation of database providers and extensions</see>
///     for more information and examples.
/// </remarks>
public readonly record struct TypeMappingInfo
{
    /// <summary>
    ///     Creates a new instance of <see cref="TypeMappingInfo" />.
    /// </summary>
    /// <param name="property">The property for which mapping is needed.</param>
    public TypeMappingInfo(IProperty property)
        : this(property.GetPrincipals())
    {
    }

    /// <summary>
    ///     Creates a new instance of <see cref="TypeMappingInfo" />.
    /// </summary>
    /// <param name="principals">The principal property chain for the property for which mapping is needed.</param>
    /// <param name="fallbackUnicode">
    ///     Specifies Unicode or ANSI for the mapping or <see langword="null" /> for default.
    /// </param>
    /// <param name="fallbackSize">
    ///     Specifies a size for the mapping, in case one isn't found at the core level, or <see langword="null" /> for default.
    /// </param>
    /// <param name="fallbackPrecision">
    ///     Specifies a precision for the mapping, in case one isn't found at the core level, or <see langword="null" /> for default.
    /// </param>
    /// <param name="fallbackScale">
    ///     Specifies a scale for the mapping, in case one isn't found at the core level, or <see langword="null" /> for default.
    /// </param>
    public TypeMappingInfo(
        IReadOnlyList<IProperty> principals,
        bool? fallbackUnicode = null,
        int? fallbackSize = null,
        int? fallbackPrecision = null,
        int? fallbackScale = null)
    {
        ValueConverter? customConverter = null;
        for (var i = 0; i < principals.Count; i++)
        {
            var principal = principals[i];
            if (customConverter == null)
            {
                var converter = principal.GetValueConverter();
                if (converter != null)
                {
                    customConverter = converter;
                }
            }

            if (fallbackSize == null)
            {
                var maxLength = principal.GetMaxLength();
                if (maxLength != null)
                {
                    fallbackSize = maxLength;
                }
            }

            if (fallbackPrecision == null)
            {
                var precisionFromProperty = principal.GetPrecision();
                if (precisionFromProperty != null)
                {
                    fallbackPrecision = precisionFromProperty;
                }
            }

            if (fallbackScale == null)
            {
                var scaleFromProperty = principal.GetScale();
                if (scaleFromProperty != null)
                {
                    fallbackScale = scaleFromProperty;
                }
            }

            if (fallbackUnicode == null)
            {
                var unicode = principal.IsUnicode();
                if (unicode != null)
                {
                    fallbackUnicode = unicode;
                }
            }
        }

        var mappingHints = customConverter?.MappingHints;
        var property = principals[0];

        IsKeyOrIndex = property.IsKey() || property.IsForeignKey() || property.IsIndex();
        Size = fallbackSize ?? mappingHints?.Size;
        IsUnicode = fallbackUnicode ?? mappingHints?.IsUnicode;
        IsRowVersion = property is { IsConcurrencyToken: true, ValueGenerated: ValueGenerated.OnAddOrUpdate };
        ClrType = (customConverter?.ProviderClrType ?? property.ClrType).UnwrapNullableType();
        Scale = fallbackScale ?? mappingHints?.Scale;
        Precision = fallbackPrecision ?? mappingHints?.Precision;
        ElementTypeMapping = null; // TODO: set from property
        JsonValueReaderWriter = property.GetJsonValueReaderWriter();
    }

    /// <summary>
    ///     Creates a new instance of <see cref="TypeMappingInfo" />.
    /// </summary>
    /// <param name="member">The property or field for which mapping is needed.</param>
    /// <param name="unicode">Specifies Unicode or ANSI mapping, or <see langword="null" /> for default.</param>
    /// <param name="size">Specifies a size for the mapping, or <see langword="null" /> for default.</param>
    /// <param name="precision">Specifies a precision for the mapping, or <see langword="null" /> for default.</param>
    /// <param name="scale">Specifies a scale for the mapping, or <see langword="null" /> for default.</param>
    public TypeMappingInfo(
        MemberInfo member,
        bool? unicode = null,
        int? size = null,
        int? precision = null,
        int? scale = null)
        : this(member.GetMemberType())
    {
        IsUnicode = unicode;
        Size = size;
        Precision = precision;
        Scale = scale;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="TypeMappingInfo" />.
    /// </summary>
    /// <param name="type">The CLR type in the model for which mapping is needed.</param>
    /// <param name="keyOrIndex">If <see langword="true" />, then a special mapping for a key or index may be returned.</param>
    /// <param name="unicode">Specifies Unicode or ANSI mapping, or <see langword="null" /> for default.</param>
    /// <param name="size">Specifies a size for the mapping, or <see langword="null" /> for default.</param>
    /// <param name="rowVersion">Specifies a row-version, or <see langword="null" /> for default.</param>
    /// <param name="precision">Specifies a precision for the mapping, or <see langword="null" /> for default.</param>
    /// <param name="scale">Specifies a scale for the mapping, or <see langword="null" /> for default.</param>
    public TypeMappingInfo(
        Type? type = null,
        bool keyOrIndex = false,
        bool? unicode = null,
        int? size = null,
        bool? rowVersion = null,
        int? precision = null,
        int? scale = null)
    {
        ClrType = type?.UnwrapNullableType();

        IsKeyOrIndex = keyOrIndex;
        Size = size;
        IsUnicode = unicode;
        IsRowVersion = rowVersion;
        Precision = precision;
        Scale = scale;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="TypeMappingInfo" /> with the given <see cref="ValueConverterInfo" />.
    /// </summary>
    /// <param name="source">The source info.</param>
    /// <param name="converter">The converter to apply.</param>
    /// <param name="unicode">Specifies Unicode or ANSI mapping, or <see langword="null" /> for default.</param>
    /// <param name="size">Specifies a size for the mapping, or <see langword="null" /> for default.</param>
    /// <param name="precision">Specifies a precision for the mapping, or <see langword="null" /> for default.</param>
    /// <param name="scale">Specifies a scale for the mapping, or <see langword="null" /> for default.</param>
    public TypeMappingInfo(
        TypeMappingInfo source,
        ValueConverterInfo converter,
        bool? unicode = null,
        int? size = null,
        int? precision = null,
        int? scale = null)
    {
        IsRowVersion = source.IsRowVersion;
        IsKeyOrIndex = source.IsKeyOrIndex;

        var mappingHints = converter.MappingHints;

        Size = size ?? source.Size ?? mappingHints?.Size;
        IsUnicode = unicode ?? source.IsUnicode ?? mappingHints?.IsUnicode;
        Scale = scale ?? source.Scale ?? mappingHints?.Scale;
        Precision = precision ?? source.Precision ?? mappingHints?.Precision;

        ClrType = converter.ProviderClrType.UnwrapNullableType();

        ElementTypeMapping = source.ElementTypeMapping;
        JsonValueReaderWriter = source.JsonValueReaderWriter;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="TypeMappingInfo" /> with the given <see cref="CoreTypeMapping" />. for collection
    ///     elements.
    /// </summary>
    /// <param name="source">The source info.</param>
    /// <param name="elementMapping">The element mapping to use.</param>
    public TypeMappingInfo(
        TypeMappingInfo source,
        CoreTypeMapping elementMapping)
    {
        IsRowVersion = source.IsRowVersion;
        IsKeyOrIndex = source.IsKeyOrIndex;
        Size = source.Size;
        IsUnicode = source.IsUnicode;
        Scale = source.Scale;
        Precision = source.Precision;
        ClrType = source.ClrType;
        ElementTypeMapping = elementMapping;
        JsonValueReaderWriter = source.JsonValueReaderWriter;
    }

    /// <summary>
    ///     Returns a new <see cref="TypeMappingInfo" /> with the given converter applied.
    /// </summary>
    /// <param name="converterInfo">The converter to apply.</param>
    /// <returns>The new mapping info.</returns>
    public TypeMappingInfo WithConverter(in ValueConverterInfo converterInfo)
        => new(this, converterInfo);

    /// <summary>
    ///     Returns a new <see cref="TypeMappingInfo" /> with the given converter applied.
    /// </summary>
    /// <param name="elementMapping">The element mapping to use.</param>
    /// <returns>The new mapping info.</returns>
    public TypeMappingInfo WithElementTypeMapping(in CoreTypeMapping elementMapping)
        => new(this, elementMapping);

    /// <summary>
    ///     Indicates whether or not the mapping is part of a key or index.
    /// </summary>
    public bool IsKeyOrIndex { get; init; }

    /// <summary>
    ///     Indicates the store-size to use for the mapping, or null if none.
    /// </summary>
    public int? Size { get; init; }

    /// <summary>
    ///     Indicates whether or not the mapping supports Unicode, or <see langword="null" /> if not defined.
    /// </summary>
    public bool? IsUnicode { get; init; }

    /// <summary>
    ///     Indicates whether or not the mapping will be used for a row version, or <see langword="null" /> if not defined.
    /// </summary>
    public bool? IsRowVersion { get; init; }

    /// <summary>
    ///     The suggested precision of the mapped data type.
    /// </summary>
    public int? Precision { get; init; }

    /// <summary>
    ///     The suggested scale of the mapped data type.
    /// </summary>
    public int? Scale { get; init; }

    /// <summary>
    ///     The CLR type in the model. May be null if type information is conveyed via other means
    ///     (e.g. the store name in a relational type mapping info)
    /// </summary>
    public Type? ClrType { get; init; }

    /// <summary>
    ///     The element type mapping, if the mapping is for a collection of primitives, or <see langword="null" /> otherwise.
    /// </summary>
    public CoreTypeMapping? ElementTypeMapping { get; init; }

    /// <summary>
    ///     The JSON reader/writer, if one has been provided, or <see langword="null" /> otherwise.
    /// </summary>
    public JsonValueReaderWriter? JsonValueReaderWriter { get; init; }
}
