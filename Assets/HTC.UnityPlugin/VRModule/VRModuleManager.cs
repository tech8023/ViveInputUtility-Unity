﻿//========= Copyright 2016-2018, HTC Corporation. All rights reserved. ===========

using HTC.UnityPlugin.Utility;
using System.Collections.Generic;
using UnityEngine;
#if VIU_STEAMVR_2_0_0_OR_NEWER
using Valve.VR;
#endif

namespace HTC.UnityPlugin.VRModuleManagement
{
    public partial class VRModule : SingletonBehaviour<VRModule>
    {
        private static readonly DeviceState s_defaultState;
        private static readonly SimulatorVRModule s_simulator;
        private static readonly Dictionary<string, uint> s_deviceSerialNumberTable;

        [SerializeField]
        private bool m_dontDestroyOnLoad = true;
        [SerializeField]
        private bool m_lockPhysicsUpdateRateToRenderFrequency = true;
        [SerializeField]
        private VRModuleSelectEnum m_selectModule = VRModuleSelectEnum.Auto;
        [SerializeField]
        private VRModuleTrackingSpaceType m_trackingSpaceType = VRModuleTrackingSpaceType.RoomScale;

        [SerializeField]
        private NewPosesEvent m_onNewPoses = new NewPosesEvent();
        [SerializeField]
        private NewInputEvent m_onNewInput = new NewInputEvent();
        [SerializeField]
        private ControllerRoleChangedEvent m_onControllerRoleChanged = new ControllerRoleChangedEvent();
        [SerializeField]
        private InputFocusEvent m_onInputFocus = new InputFocusEvent();
        [SerializeField]
        private DeviceConnectedEvent m_onDeviceConnected = new DeviceConnectedEvent();
        [SerializeField]
        private ActiveModuleChangedEvent m_onActiveModuleChanged = new ActiveModuleChangedEvent();

        private bool m_delayDeactivate = false;
        private bool m_isDestoryed = false;

        private ModuleBase[] m_modules;
        private VRModuleActiveEnum m_activatedModule = VRModuleActiveEnum.Uninitialized;
        private ModuleBase m_activatedModuleBase;
        private DeviceState[] m_prevStates;
        private DeviceState[] m_currStates;

        static VRModule()
        {
            SetDefaultInitGameObjectGetter(GetDefaultInitGameObject);

            s_defaultState = new DeviceState(INVALID_DEVICE_INDEX);
            s_simulator = new SimulatorVRModule();
        }

        private static GameObject GetDefaultInitGameObject()
        {
            return new GameObject("[ViveInputUtility]");
        }

        public static GameObject GetInstanceGameObject()
        {
            return Instance.gameObject;
        }

        protected override void OnSingletonBehaviourInitialized()
        {
            if (m_dontDestroyOnLoad && transform.parent == null && Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            m_activatedModule = VRModuleActiveEnum.Uninitialized;
            m_activatedModuleBase = null;

            m_modules = new ModuleBase[EnumUtils.GetMaxValue(typeof(VRModuleActiveEnum)) + 1];
            m_modules[(int)VRModuleActiveEnum.None] = new DefaultModule();
            m_modules[(int)VRModuleActiveEnum.Simulator] = s_simulator;
            m_modules[(int)VRModuleActiveEnum.UnityNativeVR] = new UnityEngineVRModule();
            m_modules[(int)VRModuleActiveEnum.SteamVR] = new SteamVRModule();
            m_modules[(int)VRModuleActiveEnum.OculusVR] = new OculusVRModule();
            m_modules[(int)VRModuleActiveEnum.DayDream] = new GoogleVRModule();
            m_modules[(int)VRModuleActiveEnum.WaveVR] = new WaveVRModule();
        }

        private uint GetDeviceStateLength() { return m_currStates == null ? 0u : (uint)m_currStates.Length; }

        private void EnsureDeviceStateLength(uint capacity)
        {
            // NOTE: this will clear out the array
            var cap = Mathf.Min((int)capacity, (int)MAX_DEVICE_COUNT);
            if (GetDeviceStateLength() < cap)
            {
                m_prevStates = new DeviceState[cap];
                m_currStates = new DeviceState[cap];
            }
        }

        private bool TryGetValidDeviceState(uint index, out IVRModuleDeviceState prevState, out IVRModuleDeviceStateRW currState)
        {
            if (m_currStates[index] == null)
            {
                prevState = null;
                currState = null;
                return false;
            }
            else
            {
                prevState = m_prevStates[index];
                currState = m_currStates[index];
                return true;
            }
        }

        private void EnsureValidDeviceState(uint index, out IVRModuleDeviceState prevState, out IVRModuleDeviceStateRW currState)
        {
            if (!TryGetValidDeviceState(index, out prevState, out currState))
            {
                prevState = m_prevStates[index] = new DeviceState(index);
                currState = m_currStates[index] = new DeviceState(index);
            }
        }

        private void Update()
        {
            if (!IsInstance) { return; }

            // Get should activate module
            var shouldActivateModule = GetShouldActivateModule();

            // Update module activity
            if (m_activatedModule != shouldActivateModule)
            {
                // Do clean up
                if (m_activatedModule != VRModuleActiveEnum.Uninitialized)
                {
                    DeactivateModule();
                }

                if (shouldActivateModule != VRModuleActiveEnum.Uninitialized)
                {
                    ActivateModule(shouldActivateModule);
                }
            }

            if (m_activatedModuleBase != null)
            {
                m_activatedModuleBase.Update();
            }
        }

        private void FixedUpdate()
        {
            if (!IsInstance) { return; }

            if (m_activatedModuleBase != null)
            {
                m_activatedModuleBase.FixedUpdate();
            }
        }

        private void LateUpdate()
        {
            if (!IsInstance) { return; }

            if (m_activatedModuleBase != null)
            {
                m_activatedModuleBase.LateUpdate();
            }
        }

        protected override void OnDestroy()
        {
            if (IsInstance)
            {
                m_isDestoryed = true;

                if (!m_delayDeactivate)
                {
                    DeactivateModule();
                }
            }

            base.OnDestroy();
        }

        private VRModuleActiveEnum GetShouldActivateModule()
        {
            if (m_isDestoryed) { return VRModuleActiveEnum.Uninitialized; }

            if (m_selectModule == VRModuleSelectEnum.Auto)
            {
                for (int i = m_modules.Length - 1; i >= 0; --i)
                {
                    if (m_modules[i] != null && m_modules[i].ShouldActiveModule())
                    {
                        return (VRModuleActiveEnum)i;
                    }
                }
            }
            else if (m_selectModule >= 0 && (int)m_selectModule < m_modules.Length)
            {
                return (VRModuleActiveEnum)m_selectModule;
            }
            else
            {
                return VRModuleActiveEnum.None;
            }

            return VRModuleActiveEnum.Uninitialized;
        }

        private void ActivateModule(VRModuleActiveEnum module)
        {
            if (m_activatedModule != VRModuleActiveEnum.Uninitialized)
            {
                Debug.LogError("Must deactivate before activate module! Current activatedModule:" + m_activatedModule);
                return;
            }

            if (module == VRModuleActiveEnum.Uninitialized)
            {
                Debug.LogError("Activate module cannot be Uninitialized! Use DeactivateModule instead");
                return;
            }

            m_activatedModule = module;

            m_activatedModuleBase = m_modules[(int)module];
            m_activatedModuleBase.OnActivated();

#if UNITY_2017_1_OR_NEWER
            Application.onBeforeRender += BeforeRenderUpdateModule;
#else
            Camera.onPreCull += OnCameraPreCull;
#endif

            InvokeActiveModuleChangedEvent(m_activatedModule);
        }

#if !UNITY_2017_1_OR_NEWER
        private int m_poseUpdatedFrame = -1;
        private void OnCameraPreCull(Camera cam)
        {
            var thisFrame = Time.frameCount;
            if (m_poseUpdatedFrame == thisFrame) { return; }

#if UNITY_5_5_OR_NEWER
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.VR) { return; }
#else
            if (cam.cameraType != CameraType.Game) { return; }
#endif

            m_poseUpdatedFrame = thisFrame;
            BeforeRenderUpdateModule();
        }
#endif

        private void BeforeRenderUpdateModule()
        {
            if (m_activatedModuleBase != null)
            {
                m_activatedModuleBase.BeforeRenderUpdate();
            }
        }

        private void DeactivateModule()
        {
            if (m_activatedModule == VRModuleActiveEnum.Uninitialized)
            {
                return;
            }

            if (m_activatedModuleBase == null)
            {
                return;
            }

            m_delayDeactivate = false;

#if UNITY_2017_1_OR_NEWER
            Application.onBeforeRender -= BeforeRenderUpdateModule;
#else
            Camera.onPreCull -= OnCameraPreCull;
#endif

            // copy status to from current state to previous state, and reset current state
            for (uint i = 0u, imax = GetDeviceStateLength(); i < imax; ++i)
            {
                if (m_prevStates[i].isConnected || m_currStates[i].isConnected)
                {
                    m_prevStates[i].CopyFrom(m_currStates[i]);
                    m_currStates[i].Reset();
                }
            }

            // send disconnect event
            for (uint i = 0u, imax = GetDeviceStateLength(); i < imax; ++i)
            {
                if (m_prevStates[i].isConnected)
                {
                    InvokeDeviceConnectedEvent(i, false);
                }
            }

            var deactivatedModuleBase = m_activatedModuleBase;
            m_activatedModule = VRModuleActiveEnum.Uninitialized;
            m_activatedModuleBase = null;
            deactivatedModuleBase.OnDeactivated();

            InvokeActiveModuleChangedEvent(VRModuleActiveEnum.Uninitialized);
        }

        private void ModuleFlushDeviceState()
        {
            if (m_delayDeactivate) { return; }
            m_delayDeactivate = true;

            var prevStates = m_prevStates;
            var currStates = m_currStates;

            // copy status to from current state to previous state
            for (uint i = 0u, imax = GetDeviceStateLength(); i < imax; ++i)
            {
                if (currStates[i] == null) { continue; }
                prevStates[i].CopyFrom(currStates[i]);
            }
        }

        private void ModuleConnectedDeviceChanged()
        {
            if (!m_delayDeactivate) { return; }

            var prevStates = m_prevStates;
            var currStates = m_currStates;

            m_delayDeactivate = true;
            // send connect/disconnect event
            for (uint i = 0u, imax = GetDeviceStateLength(); i < imax; ++i)
            {
                if (prevStates[i].isConnected != currStates[i].isConnected)
                {
                    if (currStates[i].isConnected)
                    {
                        if (string.IsNullOrEmpty(currStates[i].serialNumber))
                        {
                            Debug.LogError("Device connected with empty serialNumber");
                        }
                        else if (s_deviceSerialNumberTable.ContainsKey(currStates[i].serialNumber))
                        {
                            Debug.LogError("Device connected with duplicate serialNumber: " + currStates[i].serialNumber + " index:" + i + "(" + s_deviceSerialNumberTable[currStates[i].serialNumber] + ")");
                        }
                        else
                        {
                            s_deviceSerialNumberTable.Add(currStates[i].serialNumber, i);
                        }

                        InvokeDeviceConnectedEvent(i, true);
                    }
                    else
                    {
                        s_deviceSerialNumberTable.Remove(prevStates[i].serialNumber);

                        InvokeDeviceConnectedEvent(i, false);
                    }
                }
            }

            m_delayDeactivate = false;
            if (m_isDestoryed)
            {
                DeactivateModule();
            }
        }
    }
}