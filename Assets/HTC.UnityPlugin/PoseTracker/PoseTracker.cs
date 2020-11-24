﻿//========= Copyright 2016-2020, HTC Corporation. All rights reserved. ===========

using HTC.UnityPlugin.Utility;
using UnityEngine;

namespace HTC.UnityPlugin.PoseTracker
{
    public class PoseTracker : BasePoseTracker
    {
        public Transform target;

        protected virtual void LateUpdate()
        {
            if (target == null)
            {
                TrackPose(RigidPose.identity, true);
            }
            else
            {
                TrackPose(new RigidPose(target), false);
            }
        }
    }
}