﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using Umbraco.Core.Exceptions;
using Umbraco.Core.Models.Entities;

namespace Umbraco.Core.Models
{
    /// <summary>
    /// Represents an abstract class for base Content properties and methods
    /// </summary>
    [Serializable]
    [DataContract(IsReference = true)]
    [DebuggerDisplay("Id: {Id}, Name: {Name}, ContentType: {ContentTypeBase.Alias}")]
    public abstract class ContentBase : TreeEntityBase, IContentBase
    {

        private int _contentTypeId;
        protected IContentTypeComposition ContentTypeBase;
        private int _writerId;
        private PropertyCollection _properties;
        private ContentCultureInfosCollection _cultureInfos;

        #region Used for change tracking

        private (HashSet<string> addedCultures, HashSet<string> removedCultures, HashSet<string> updatedCultures) _currentCultureChanges;
        private (HashSet<string> addedCultures, HashSet<string> removedCultures, HashSet<string> updatedCultures) _previousCultureChanges;

        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentBase"/> class.
        /// </summary>
        protected ContentBase(string name, int parentId, IContentTypeComposition contentType, PropertyCollection properties, string culture = null)
            : this(name, contentType, properties, culture)
        {
            if (parentId == 0) throw new ArgumentOutOfRangeException(nameof(parentId));
            ParentId = parentId;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ContentBase"/> class.
        /// </summary>
        protected ContentBase(string name, IContentBase parent, IContentTypeComposition contentType, PropertyCollection properties, string culture = null)
            : this(name, contentType, properties, culture)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            SetParent(parent);
        }

        private ContentBase(string name, IContentTypeComposition contentType, PropertyCollection properties, string culture = null)
        {
            ContentTypeBase = contentType ?? throw new ArgumentNullException(nameof(contentType));

            // initially, all new instances have
            Id = 0; // no identity
            VersionId = 0; // no versions

            SetCultureName(name, culture);

            _contentTypeId = contentType.Id;
            _properties = properties ?? throw new ArgumentNullException(nameof(properties));
            _properties.EnsurePropertyTypes(PropertyTypes);
        }

        protected void PropertiesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Properties));
        }



        /// <summary>
        /// Id of the user who wrote/updated this entity
        /// </summary>
        [DataMember]
        public int WriterId
        {
            get => _writerId;
            set => SetPropertyValueAndDetectChanges(value, ref _writerId, nameof(WriterId));
        }

        [IgnoreDataMember]
        public int VersionId { get; set; }

        /// <summary>
        /// Integer Id of the default ContentType
        /// </summary>
        [DataMember]
        public int ContentTypeId
        {
            get
            {
                //There will be cases where this has not been updated to reflect the true content type ID.
                //This will occur when inserting new content.
                if (_contentTypeId == 0 && ContentTypeBase != null && ContentTypeBase.HasIdentity)
                {
                    _contentTypeId = ContentTypeBase.Id;
                }
                return _contentTypeId;
            }
            protected set => SetPropertyValueAndDetectChanges(value, ref _contentTypeId, nameof(ContentTypeId));
        }

        /// <summary>
        /// Gets or sets the collection of properties for the entity.
        /// </summary>
        /// <remarks>
        /// Marked DoNotClone since we'll manually clone the underlying field to deal with the event handling
        /// </remarks>
        [DataMember]
        [DoNotClone]
        public PropertyCollection Properties
        {
            get => _properties;
            set
            {
                if (_properties != null) _properties.CollectionChanged -= PropertiesChanged;
                _properties = value;
                _properties.CollectionChanged += PropertiesChanged;
            }
        }

        /// <summary>
        /// Gets the enumeration of property groups for the entity.
        /// TODO: remove this proxy method
        /// </summary>
        [IgnoreDataMember]
        public IEnumerable<PropertyGroup> PropertyGroups => ContentTypeBase.CompositionPropertyGroups;

        /// <summary>
        /// Gets the numeration of property types for the entity.
        /// TODO: remove this proxy method
        /// </summary>
        [IgnoreDataMember]
        public IEnumerable<PropertyType> PropertyTypes => ContentTypeBase.CompositionPropertyTypes;

        #region Cultures

        // notes - common rules
        // - setting a variant value on an invariant content type throws
        // - getting a variant value on an invariant content type returns null
        // - setting and getting the invariant value is always possible
        // - setting a null value clears the value

        /// <inheritdoc />
        public IEnumerable<string> AvailableCultures
            => _cultureInfos?.Keys ?? Enumerable.Empty<string>();

        /// <inheritdoc />
        public bool IsCultureAvailable(string culture)
            => _cultureInfos != null && _cultureInfos.ContainsKey(culture);

        /// <inheritdoc />
        [DataMember]
        public ContentCultureInfosCollection CultureInfos
        {
            get
            {
                if (_cultureInfos != null) return _cultureInfos;
                _cultureInfos = new ContentCultureInfosCollection();
                _cultureInfos.CollectionChanged += CultureInfosCollectionChanged;
                return _cultureInfos;
            }
            set
            {
                if (_cultureInfos != null) _cultureInfos.CollectionChanged -= CultureInfosCollectionChanged;
                _cultureInfos = value;
                if (_cultureInfos != null)
                    _cultureInfos.CollectionChanged += CultureInfosCollectionChanged;
            }
        }

        /// <inheritdoc />
        public string GetCultureName(string culture)
        {
            if (culture.IsNullOrWhiteSpace()) return Name;
            if (!ContentTypeBase.VariesByCulture()) return null;
            if (_cultureInfos == null) return null;
            return _cultureInfos.TryGetValue(culture, out var infos) ? infos.Name : null;
        }

        /// <inheritdoc />
        public DateTime? GetUpdateDate(string culture)
        {
            if (culture.IsNullOrWhiteSpace()) return null;
            if (!ContentTypeBase.VariesByCulture()) return null;
            if (_cultureInfos == null) return null;
            return _cultureInfos.TryGetValue(culture, out var infos) ? infos.Date : (DateTime?)null;
        }

        /// <inheritdoc />
        public void SetCultureName(string name, string culture)
        {
            if (ContentTypeBase.VariesByCulture()) // set on variant content type
            {
                if (culture.IsNullOrWhiteSpace()) // invariant is ok
                {
                    Name = name; // may be null
                }
                else if (name.IsNullOrWhiteSpace()) // clear
                {
                    ClearCultureInfo(culture);
                }
                else // set
                {
                    this.SetCultureInfo(culture, name, DateTime.Now);
                }
            }
            else // set on invariant content type
            {
                if (!culture.IsNullOrWhiteSpace()) // invariant is NOT ok
                    throw new NotSupportedException("Content type does not vary by culture.");

                Name = name; // may be null
            }
        }

        private void ClearCultureInfo(string culture)
        {
            if (culture.IsNullOrWhiteSpace())
                throw new ArgumentNullOrEmptyException(nameof(culture));

            if (_cultureInfos == null) return;
            _cultureInfos.Remove(culture);
            if (_cultureInfos.Count == 0)
                _cultureInfos = null;
        }

        /// <summary>
        /// Handles culture infos collection changes.
        /// </summary>
        private void CultureInfosCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CultureInfos));

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    {
                        var cultureInfo = e.NewItems.Cast<ContentCultureInfos>().First();
                        if (_currentCultureChanges.addedCultures == null) _currentCultureChanges.addedCultures = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                        if (_currentCultureChanges.updatedCultures == null) _currentCultureChanges.updatedCultures = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                        _currentCultureChanges.addedCultures.Add(cultureInfo.Culture);
                        _currentCultureChanges.updatedCultures.Add(cultureInfo.Culture);
                        _currentCultureChanges.removedCultures?.Remove(cultureInfo.Culture);
                        break;
                    }
                case NotifyCollectionChangedAction.Remove:
                    {
                        //remove listening for changes
                        var cultureInfo = e.OldItems.Cast<ContentCultureInfos>().First();
                        if (_currentCultureChanges.removedCultures == null) _currentCultureChanges.removedCultures = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                        _currentCultureChanges.removedCultures.Add(cultureInfo.Culture);
                        _currentCultureChanges.updatedCultures?.Remove(cultureInfo.Culture);
                        _currentCultureChanges.addedCultures?.Remove(cultureInfo.Culture);
                        break;
                    }
                case NotifyCollectionChangedAction.Replace:
                    {
                        //replace occurs when an Update occurs
                        var cultureInfo = e.NewItems.Cast<ContentCultureInfos>().First();
                        if (_currentCultureChanges.updatedCultures == null) _currentCultureChanges.updatedCultures = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                        _currentCultureChanges.updatedCultures.Add(cultureInfo.Culture);
                        break;
                    }
            }
        }

        #endregion

        #region Has, Get, Set, Publish Property Value

        /// <inheritdoc />
        public bool HasProperty(string propertyTypeAlias)
            => Properties.Contains(propertyTypeAlias);

        /// <inheritdoc />
        public object GetValue(string propertyTypeAlias, string culture = null, string segment = null, bool published = false)
        {
            return Properties.TryGetValue(propertyTypeAlias, out var property)
                ? property.GetValue(culture, segment, published)
                : null;
        }

        /// <inheritdoc />
        public TValue GetValue<TValue>(string propertyTypeAlias, string culture = null, string segment = null, bool published = false)
        {
            if (!Properties.TryGetValue(propertyTypeAlias, out var property))
                return default;

            var convertAttempt = property.GetValue(culture, segment, published).TryConvertTo<TValue>();
            return convertAttempt.Success ? convertAttempt.Result : default;
        }

        /// <inheritdoc />
        public void SetValue(string propertyTypeAlias, object value, string culture = null, string segment = null)
        {
            if (Properties.Contains(propertyTypeAlias))
            {
                Properties[propertyTypeAlias].SetValue(value, culture, segment);
                //bump the culture to be flagged for updating
                this.TouchCulture(culture);
                return;
            }

            var propertyType = PropertyTypes.FirstOrDefault(x => x.Alias.InvariantEquals(propertyTypeAlias));
            if (propertyType == null)
                throw new InvalidOperationException($"No PropertyType exists with the supplied alias \"{propertyTypeAlias}\".");

            var property = propertyType.CreateProperty();
            property.SetValue(value, culture, segment);
            Properties.Add(property);

            //bump the culture to be flagged for updating
            this.TouchCulture(culture);
        }

        #endregion

        #region Dirty

        public override void ResetWereDirtyProperties()
        {
            base.ResetWereDirtyProperties();
            _previousCultureChanges.addedCultures = null;
            _previousCultureChanges.removedCultures = null;
            _previousCultureChanges.updatedCultures = null;
        }

        /// <inheritdoc />
        /// <remarks>Overridden to include user properties.</remarks>
        public override void ResetDirtyProperties(bool rememberDirty)
        {
            base.ResetDirtyProperties(rememberDirty);

            if (rememberDirty)
            {
                _previousCultureChanges.addedCultures = _currentCultureChanges.addedCultures == null ? null : new HashSet<string>(_currentCultureChanges.addedCultures, StringComparer.InvariantCultureIgnoreCase);
                _previousCultureChanges.removedCultures = _currentCultureChanges.removedCultures == null ? null : new HashSet<string>(_currentCultureChanges.removedCultures, StringComparer.InvariantCultureIgnoreCase);
                _previousCultureChanges.updatedCultures = _currentCultureChanges.updatedCultures == null ? null : new HashSet<string>(_currentCultureChanges.updatedCultures, StringComparer.InvariantCultureIgnoreCase);
            }
            else
            {
                _previousCultureChanges.addedCultures = null;
                _previousCultureChanges.removedCultures = null;
                _previousCultureChanges.updatedCultures = null;
            }
            _currentCultureChanges.addedCultures?.Clear();
            _currentCultureChanges.removedCultures?.Clear();
            _currentCultureChanges.updatedCultures?.Clear();

            // also reset dirty changes made to user's properties
            foreach (var prop in Properties)
                prop.ResetDirtyProperties(rememberDirty);

            // take care of culture infos
            if (_cultureInfos == null) return;

            foreach (var cultureInfo in _cultureInfos)
                cultureInfo.ResetDirtyProperties(rememberDirty);
        }

        /// <inheritdoc />
        /// <remarks>Overridden to include user properties.</remarks>
        public override bool IsDirty()
        {
            return IsEntityDirty() || this.IsAnyUserPropertyDirty();
        }

        /// <inheritdoc />
        /// <remarks>Overridden to include user properties.</remarks>
        public override bool WasDirty()
        {
            return WasEntityDirty() || this.WasAnyUserPropertyDirty();
        }

        /// <summary>
        /// Gets a value indicating whether the current entity's own properties (not user) are dirty.
        /// </summary>
        public bool IsEntityDirty()
        {
            return base.IsDirty();
        }

        /// <summary>
        /// Gets a value indicating whether the current entity's own properties (not user) were dirty.
        /// </summary>
        public bool WasEntityDirty()
        {
            return base.WasDirty();
        }

        /// <inheritdoc />
        /// <remarks>Overridden to include user properties.</remarks>
        public override bool IsPropertyDirty(string propertyName)
        {
            if (base.IsPropertyDirty(propertyName))
                return true;

            //Special check here since we want to check if the request is for changed cultures
            if (propertyName.StartsWith("_addedCulture_"))
            {
                var culture = propertyName.TrimStart("_addedCulture_");
                return _currentCultureChanges.addedCultures?.Contains(culture) ?? false;
            }
            if (propertyName.StartsWith("_removedCulture_"))
            {
                var culture = propertyName.TrimStart("_removedCulture_");
                return _currentCultureChanges.removedCultures?.Contains(culture) ?? false;
            }
            if (propertyName.StartsWith("_updatedCulture_"))
            {
                var culture = propertyName.TrimStart("_updatedCulture_");
                return _currentCultureChanges.updatedCultures?.Contains(culture) ?? false;
            }

            return Properties.Contains(propertyName) && Properties[propertyName].IsDirty();
        }

        /// <inheritdoc />
        /// <remarks>Overridden to include user properties.</remarks>
        public override bool WasPropertyDirty(string propertyName)
        {
            if (base.WasPropertyDirty(propertyName))
                return true;

            //Special check here since we want to check if the request is for changed cultures
            if (propertyName.StartsWith("_addedCulture_"))
            {
                var culture = propertyName.TrimStart("_addedCulture_");
                return _previousCultureChanges.addedCultures?.Contains(culture) ?? false;
            }
            if (propertyName.StartsWith("_removedCulture_"))
            {
                var culture = propertyName.TrimStart("_removedCulture_");
                return _previousCultureChanges.removedCultures?.Contains(culture) ?? false;
            }
            if (propertyName.StartsWith("_updatedCulture_"))
            {
                var culture = propertyName.TrimStart("_updatedCulture_");
                return _previousCultureChanges.updatedCultures?.Contains(culture) ?? false;
            }

            return Properties.Contains(propertyName) && Properties[propertyName].WasDirty();
        }

        /// <inheritdoc />
        /// <remarks>Overridden to include user properties.</remarks>
        public override IEnumerable<string> GetDirtyProperties()
        {
            var instanceProperties = base.GetDirtyProperties();
            var propertyTypes = Properties.Where(x => x.IsDirty()).Select(x => x.Alias);
            return instanceProperties.Concat(propertyTypes);
        }

        /// <inheritdoc />
        /// <remarks>Overridden to include user properties.</remarks>
        public override IEnumerable<string> GetWereDirtyProperties()
        {
            var instanceProperties = base.GetWereDirtyProperties();
            var propertyTypes = Properties.Where(x => x.WasDirty()).Select(x => x.Alias);
            return instanceProperties.Concat(propertyTypes);
        }

        #endregion

        /// <inheritdoc />
        /// <remarks>
        /// Overridden to deal with specific object instances
        /// </remarks>
        protected override void PerformDeepClone(object clone)
        {
            base.PerformDeepClone(clone);

            var clonedContent = (ContentBase)clone;

            //fixme - need to reset change tracking bits

            //if culture infos exist then deal with event bindings
            if (clonedContent._cultureInfos != null)
            {
                clonedContent._cultureInfos.CollectionChanged -= CultureInfosCollectionChanged;          //clear this event handler if any
                clonedContent._cultureInfos = (ContentCultureInfosCollection)_cultureInfos.DeepClone(); //manually deep clone
                clonedContent._cultureInfos.CollectionChanged += clonedContent.CultureInfosCollectionChanged;    //re-assign correct event handler
            }

            //if properties exist then deal with event bindings
            if (clonedContent._properties != null)
            {
                clonedContent._properties.CollectionChanged -= PropertiesChanged;         //clear this event handler if any
                clonedContent._properties = (PropertyCollection)_properties.DeepClone(); //manually deep clone
                clonedContent._properties.CollectionChanged += clonedContent.PropertiesChanged;   //re-assign correct event handler
            }

            clonedContent._currentCultureChanges.updatedCultures = null;
            clonedContent._currentCultureChanges.addedCultures = null;
            clonedContent._currentCultureChanges.removedCultures = null;

            clonedContent._previousCultureChanges.updatedCultures = null;
            clonedContent._previousCultureChanges.addedCultures = null;
            clonedContent._previousCultureChanges.removedCultures = null;
        }
    }
}
