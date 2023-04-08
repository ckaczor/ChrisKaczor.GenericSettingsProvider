using System.Collections.Specialized;
using System.Configuration;
using System.Reflection;

namespace ChrisKaczor.GenericSettingsProvider
{
    public class GenericSettingsProvider : SettingsProvider, IApplicationSettingsProvider
    {
        #region Delegates

        public delegate object OpenDataStoreDelegate();

        public delegate void CloseDataStoreDelegate(object dataStore);

        public delegate string GetSettingValueDelegate(object dataStore, string name, Version version);

        public delegate void SetSettingValueDelegate(object dataStore, string name, Version version, string value);

        public delegate List<Version> GetVersionListDelegate(object dataStore);

        public delegate void DeleteSettingsForVersionDelegate(object dataStore, Version version);

        #endregion

        #region Callbacks

        public OpenDataStoreDelegate? OpenDataStore = null;
        public CloseDataStoreDelegate? CloseDataStore = null;
        public GetSettingValueDelegate? GetSettingValue = null;
        public SetSettingValueDelegate? SetSettingValue = null;
        public GetVersionListDelegate? GetVersionList = null;
        public DeleteSettingsForVersionDelegate? DeleteSettingsForVersion = null;

        #endregion

        #region SettingsProvider members

        public override string ApplicationName { get; set; } = string.Empty;

        public bool DeleteOldVersionsOnUpgrade { get; set; } = false;

        public override void Initialize(string name, NameValueCollection config)
        {
            if (string.IsNullOrEmpty(name))
                name = GetType().Name;

            base.Initialize(name, config);
        }

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection properties)
        {
            // Create a new collection for the values
            var values = new SettingsPropertyValueCollection();

            // Get the current version number
            var version = GetCurrentVersion();

            // Open the data store
            var dataStore = OpenDataStore!();

            // Loop over each property
            foreach (SettingsProperty property in properties)
            {
                // Get the setting value for the current version
                var value = GetPropertyValue(dataStore, property, version);

                // Add the value to the collection
                values.Add(value);
            }

            // Close the data store
            CloseDataStore?.Invoke(dataStore);

            return values;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection properties)
        {
            // Get the current version number
            var version = GetCurrentVersion();

            // Open the data store
            var dataStore = OpenDataStore!();

            // Loop over each property
            foreach (SettingsPropertyValue propertyValue in properties)
            {
                // If the property isn't dirty or it is null then we can skip it
                if (!propertyValue.IsDirty || (propertyValue.SerializedValue == null))
                    continue;

                // Set the property value
                SetPropertyValue(dataStore, propertyValue, version);
            }

            // Close the data store
            CloseDataStore?.Invoke(dataStore);
        }

        #endregion

        #region Version numbers

        private static Version GetCurrentVersion()
        {
            return Assembly.GetEntryAssembly()!.GetName().Version ?? new Version();
        }

        private Version? GetPreviousVersion(object dataStore)
        {
            // Get the current version number
            var currentVersion = GetCurrentVersion();

            // Get a distinct list of version numbers 
            var versionList = GetVersionList?.Invoke(dataStore);

            // Sort the list using the Version object and get the first value
            var previousVersion = versionList?.Where(v => v < currentVersion).MaxBy(v => v);

            return previousVersion;
        }

        #endregion

        #region Value get and set

        private SettingsPropertyValue GetPropertyValue(object dataStore, SettingsProperty property, Version version)
        {
            // Create the value for the property
            var value = new SettingsPropertyValue(property);

            // Try to get the setting that matches the name and version
            var setting = GetSettingValue!(dataStore, property.Name, version);

            // If the setting was found then set the value, otherwise leave as default
            value.SerializedValue = setting;

            // Value is not dirty since we just read it
            value.IsDirty = false;

            return value;
        }

        private void SetPropertyValue(object dataStore, SettingsPropertyValue value, Version version)
        {
            // Set the value for this version
            SetSettingValue!(dataStore, value.Property.Name, version, value.SerializedValue.ToString() ?? string.Empty);
        }

        #endregion

        #region IApplicationSettingsProvider members

        public void Reset(SettingsContext context)
        {
            // Get the current version number
            var version = GetCurrentVersion();

            // Open the data store
            var dataStore = OpenDataStore!();

            // Delete all settings for this version
            DeleteSettingsForVersion?.Invoke(dataStore, version);

            // Close the data store
            CloseDataStore?.Invoke(dataStore);
        }

        public SettingsPropertyValue GetPreviousVersion(SettingsContext context, SettingsProperty property)
        {
            // Open the data store
            var dataStore = OpenDataStore!();

            // Get the previous version number
            var previousVersion = GetPreviousVersion(dataStore);

            SettingsPropertyValue value;

            // If there is no previous version we have a return a setting with a null value
            if (previousVersion == null)
            {
                // Create a new property value with the value set to null
                value = new SettingsPropertyValue(property) { PropertyValue = null };

                return value;
            }

            // Return the value from the previous version
            value = GetPropertyValue(dataStore, property, previousVersion);

            // Close the data store
            CloseDataStore?.Invoke(dataStore);

            return value;
        }

        public void Upgrade(SettingsContext context, SettingsPropertyCollection properties)
        {
            // Open the data store
            var dataStore = OpenDataStore!();

            // Get the previous version number
            var previousVersion = GetPreviousVersion(dataStore);

            // If there is no previous version number just do nothing
            if (previousVersion == null)
                return;

            // Delete everything for the current version
            Reset(context);

            // Get the current version number
            var currentVersion = GetCurrentVersion();

            // Loop over each property
            foreach (SettingsProperty property in properties)
            {
                // Get the previous value
                var previousValue = GetPropertyValue(dataStore, property, previousVersion);

                // Set the current value if there was a previous value
                if (previousValue.SerializedValue != null)
                    SetPropertyValue(dataStore, previousValue, currentVersion);
            }

            if (DeleteOldVersionsOnUpgrade)
            {
                // Get a distinct list of version numbers 
                var versionList = GetVersionList!(dataStore);

                // Get everything before the current version
                versionList = versionList.Where(v => v < currentVersion).ToList();

                // Delete settings for anything in the list
                foreach (var version in versionList)
                    DeleteSettingsForVersion!(dataStore, version);
            }

            // Close the data store
            CloseDataStore?.Invoke(dataStore);
        }

        #endregion
    }
}