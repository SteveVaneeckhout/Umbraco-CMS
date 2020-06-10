﻿using System;
using Umbraco.Core.Models.PublishedContent;

namespace Umbraco.Core.PropertyEditors
{
    /// <summary>
    /// Provides a default overridable implementation for <see cref="IPropertyValueConverter"/> that does nothing.
    /// </summary>
    public abstract class PropertyValueConverterBase : IPropertyValueConverter
    {
        public virtual bool IsConverter(IPublishedPropertyType propertyType)
            => false;

        public virtual bool? IsValue(object value, PropertyValueLevel level)
        {
            switch (level)
            {
                case PropertyValueLevel.Source:
                    // the default implementation uses the old magic null & string comparisons,
                    // other implementations may be more clever, and/or test the final converted object values
                    return value != null && (!(value is string stringValue) || !string.IsNullOrWhiteSpace(stringValue));
                case PropertyValueLevel.Inter:
                    return null;
                case PropertyValueLevel.Object:
                    return null;
                default:
                    throw new NotSupportedException($"Invalid level: {level}.");
            }
        }

        [Obsolete("This method is not part of the IPropertyValueConverter contract and therefore not used, use IsValue instead.")]
        public virtual bool HasValue(IPublishedProperty property, string culture, string segment)
        {
            var value = property.GetSourceValue(culture, segment);
            return value != null && (!(value is string stringValue) || !string.IsNullOrWhiteSpace(stringValue));
        }

        public virtual Type GetPropertyValueType(IPublishedPropertyType propertyType)
            => typeof(object);

        public virtual PropertyCacheLevel GetPropertyCacheLevel(IPublishedPropertyType propertyType)
            => PropertyCacheLevel.Snapshot;

        public virtual object ConvertSourceToIntermediate(IPublishedElement owner, IPublishedPropertyType propertyType, object source, bool preview)
            => source;

        public virtual object ConvertIntermediateToObject(IPublishedElement owner, IPublishedPropertyType propertyType, PropertyCacheLevel referenceCacheLevel, object inter, bool preview)
            => inter;

        public virtual object ConvertIntermediateToXPath(IPublishedElement owner, IPublishedPropertyType propertyType, PropertyCacheLevel referenceCacheLevel, object inter, bool preview)
            => inter?.ToString() ?? string.Empty;
    }
}
