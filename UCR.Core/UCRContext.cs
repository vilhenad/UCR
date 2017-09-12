﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using IOWrapper;
using UCR.Core.Device;
using UCR.Core.Utilities;

namespace UCR.Core
{
    public class UCRContext
    {
        private static string _contextName = "context.xml";

        // Persistence
        public List<Profile.Profile> Profiles { get; set; }

        public List<DeviceGroup> KeyboardGroups { get; set; }
        public List<DeviceGroup> MiceGroups { get; set; }
        public List<DeviceGroup> JoystickGroups { get; set; }
        public List<DeviceGroup> GenericDeviceGroups { get; set; }

        // Runtime
        [XmlIgnore]
        public bool IsNotSaved { get; set; }

        [XmlIgnore]
        public Profile.Profile ActiveProfile { get; set; }

        [XmlIgnore]
        public IOController IOController { get; set; }

        [XmlIgnore] private List<Action> ActiveProfileCallbacks = new List<Action>();

        public UCRContext()
        {
            Init();
        }

        private void Init()
        {
            IsNotSaved = false;
            IOController = new IOController();
            Profiles = new List<Profile.Profile>();
            KeyboardGroups = new List<DeviceGroup>();
            MiceGroups = new List<DeviceGroup>();
            JoystickGroups = new List<DeviceGroup>();
            GenericDeviceGroups = new List<DeviceGroup>();
        }


        #region Profile

        public bool AddProfile(string title)
        {
            Profiles.Add(Profile.Profile.CreateProfile(this, title));
            IsNotSaved = true;
            return true;
        }

        private Profile.Profile GetGlobalProfile()
        {
            // TODO Find it properly
            return Profiles.Find(p => p.Title.Equals("Global"));
        }

        public bool ActivateProfile(Profile.Profile profile)
        {
            var success = true;
            if (ActiveProfile?.Guid == profile.Guid) return success;
            var lastActiveProfile = ActiveProfile;
            ActiveProfile = profile;
            success &= profile.Activate(this);
            if (success)
            {
                var subscribeSuccess = profile.SubscribeDeviceLists();
                IOController.SetProfileState(profile.Guid, true);
                DeactivateProfile(lastActiveProfile);
                foreach (var action in ActiveProfileCallbacks)
                {
                    action();
                }
            }
            else
            {
                // Activation failed, old profile is still active
                ActiveProfile = lastActiveProfile;
            }
            return success;
        }

        public bool DeactivateProfile(Profile.Profile profile)
        {
            if (ActiveProfile == null || profile == null) return true;
            if (ActiveProfile.Guid == profile.Guid) ActiveProfile = null;

            var success = profile.UnsubscribeDeviceLists();
            IOController.SetProfileState(profile.Guid, false);

            foreach (var action in ActiveProfileCallbacks)
            {
                action();
            }
            return success;
        }

        #endregion

        #region DeviceGroup

        public DeviceGroup GetDeviceGroup(DeviceType deviceType, Guid deviceGroupGuid)
        {
            return GetDeviceGroupList(deviceType).FirstOrDefault(d => d.Guid == deviceGroupGuid);
        }

        public Guid AddDeviceGroup(string Title, DeviceType deviceType)
        {
            var deviceGroup = new DeviceGroup(Title);
            GetDeviceGroupList(deviceType).Add(deviceGroup);
            IsNotSaved = true;
            return deviceGroup.Guid;
        }

        public bool RemoveDeviceGroup(Guid deviceGroupGuid, DeviceType deviceType)
        {
            var deviceGroups = GetDeviceGroupList(deviceType);
            if (!deviceGroups.Remove(DeviceGroup.FindDeviceGroup(deviceGroups, deviceGroupGuid))) return false;
            IsNotSaved = true;
            return true;
        }

        public bool RenameDeviceGroup(Guid deviceGroupGuid, DeviceType deviceType, string title)
        {
            var deviceGroups = GetDeviceGroupList(deviceType);
            DeviceGroup.FindDeviceGroup(deviceGroups, deviceGroupGuid).Title = title;
            IsNotSaved = true;
            return true;
        }

        public void AddDeviceToDeviceGroup(Device.Device device, DeviceType deviceType, Guid deviceGroupGuid)
        {
            GetDeviceGroupList(deviceType).First(d => d.Guid == deviceGroupGuid).Devices.Add(device);
            IsNotSaved = true;
        }

        public void RemoveDeviceFromDeviceGroup(Device.Device device, DeviceType deviceType, Guid deviceGroupGuid)
        {
            GetDeviceGroup(deviceType, deviceGroupGuid).RemoveDevice(device.Guid);
            IsNotSaved = true;
        }

        public List<DeviceGroup> GetDeviceGroupList(DeviceType deviceType)
        {
            switch (deviceType)
            {
                case DeviceType.Joystick:
                    return JoystickGroups;
                case DeviceType.Keyboard:
                    return KeyboardGroups;
                case DeviceType.Mouse:
                    return MiceGroups;
                case DeviceType.Generic:
                    return GenericDeviceGroups;
                default:
                    throw new ArgumentOutOfRangeException(nameof(deviceType), deviceType, null);
            }
        }

        #endregion

        public void SetActiveProfileCallback(Action profileActivated)
        {
            ActiveProfileCallbacks.Add(profileActivated);
        }

        #region Persistence

        // TODO refactor to this instead of setting bool explicitly
        public void ContextChanged()
        {
            IsNotSaved = true;
        }

        public bool SaveContext()
        {
            var serializer = GetXmlSerializer();
            using (var streamWriter = new StreamWriter(_contextName))
            {
                serializer.Serialize(streamWriter, this);
            }
            IsNotSaved = false;
            return true;
        }

        public static UCRContext Load()
        {
            UCRContext ctx;
            var serializer = GetXmlSerializer();
            try
            {
                using (var fileStream = new FileStream(_contextName, FileMode.Open))
                {
                    ctx = (UCRContext) serializer.Deserialize(fileStream);
                    ctx.PostLoad();
                }
            }
            catch (IOException e)
            {
                Console.Write(e.ToString());
                // TODO log exception
                ctx = new UCRContext();
            }
            return ctx;
        }

        private void PostLoad()
        {
            foreach (var profile in Profiles)
            {
                profile.PostLoad(this);
            }
        }

        private static XmlSerializer GetXmlSerializer()
        {
            return new XmlSerializer(typeof(UCRContext),
                Toolbox.GetEnumerableOfType<Plugin.Plugin>().Select(p => p.GetType()).ToArray());
        }

        #endregion
    }
}