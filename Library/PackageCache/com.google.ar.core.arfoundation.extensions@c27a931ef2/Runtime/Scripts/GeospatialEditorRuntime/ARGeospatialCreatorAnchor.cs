//-----------------------------------------------------------------------
// <copyright file="ARGeospatialCreatorAnchor.cs" company="Google LLC">
//
// Copyright 2023 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// </copyright>
//-----------------------------------------------------------------------
#if UNITY_2021_3_OR_NEWER

namespace Google.XR.ARCoreExtensions.GeospatialCreator.Internal
{
    using System;
    using System.Collections;
    using Google.XR.ARCoreExtensions.Internal;
#if ARCORE_INTERNAL_USE_UNITY_MATH
    using Unity.Mathematics;
#endif
    using UnityEngine;
    using UnityEngine.XR.ARFoundation;
    using UnityEngine.XR.ARSubsystems;

    [AddComponentMenu("XR/AR Geospatial Creator Anchor")]
    [ExecuteInEditMode]
    public class ARGeospatialCreatorAnchor : MonoBehaviour
    {
        public enum AltitudeType
        {
            ManualAltitude,
            Terrain,
            Rooftop,
        };

#if !UNITY_EDITOR
        private enum AnchorResolutionState {
            NotStarted,
            InProgress,
            Complete
        };
#endif

#if UNITY_EDITOR
        public GeoTilesReference GeoReference;

        public double3 ECEF;
        public double3 EUN;
        public double3 EUS;

        public bool shouldPosition = true;
#endif

        public AltitudeType AltType = AltitudeType.ManualAltitude;

        public double Latitude
        {
            get => this._latitude;
            set { this._latitude = value; }
        }

        public double Longitude
        {
            get => this._longitude;
            set { this._longitude = value; }
        }

        public double Altitude
        {
            get => this._altitude;
            set { this._altitude = value; }
        }

        public double AltitudeOffset
        {
            get => this._altitudeOffset;
            set { this._altitudeOffset = value; }
        }

        [SerializeField]
        private double _latitude;

        [SerializeField]
        private double _longitude;

        [SerializeField]
        private double _altitude;

        [SerializeField]
        private double _altitudeOffset;

#if !UNITY_EDITOR
        private AnchorResolutionState _anchorResolution = AnchorResolutionState.NotStarted;
#endif

#if UNITY_EDITOR
        [SerializeField]
        private double _geoRefLatitude;

        [SerializeField]
        private double _geoRefLongitude;

        [SerializeField]
        private double _geoRefHeight;

        private double4x4 ENUToECEF;
        private double4x4 ECEFToENU;
        private Vector3 _oldPosition = Vector3.zero;
        private Quaternion _oldRotation = Quaternion.identity;
        private Vector3 _oldScale = Vector3.one;

        public void SetUnityPosition()
        {
            shouldPosition = false;
            CalculateRealWorldPosition();
            GeoCoor coor = new GeoCoor(Latitude, Longitude, Altitude);
            double3 localInECEF = GeoCoor.GeoCoorToECEF(coor);
            double3 ENU = MatrixStack.MultPoint(ECEFToENU, localInECEF);
            // Unity is EUN not ENU so swap z and y
            Vector3 EUN = new Vector3((float)ENU.x, (float)ENU.z, (float)ENU.y);
            transform.position = EUN;
        }

        private void CalculateRealWorldPosition()
        {
            GeoCoor coor;
            GeoTilesReferencePoint refPoint = GeoReference.GetTilesReferencePoint(this.gameObject);
            if (refPoint != null)
            {
                coor = new GeoCoor(refPoint.Latitude, refPoint.Longitude, refPoint.Height);
            }
            else
            {
                coor = new GeoCoor(_geoRefLatitude, _geoRefLongitude, _geoRefHeight);
            }

            // :TODO b/277370107: This could be optimized by only changing the position if the
            // object or origin has moved
            double3 PositionInECEF = GeoCoor.GeoCoorToECEF(coor);

            // Rotate from y up to z up and flip X. References:
            //   https://github.com/CesiumGS/3d-tiles/tree/main/specification#transforms
            //   https://stackoverflow.com/questions/1263072/changing-a-matrix-from-right-handed-to-left-handed-coordinate-system
            //   https://en.wikipedia.org/wiki/Geographic_coordinate_conversion#From_ECEF_to_ENU
            MatrixStack _matrixStack = new MatrixStack();
            _matrixStack.PushMatrix();

            double latSin, latCos;
            math.sincos(coor.Latitude / 180 * Math.PI, out latSin, out latCos);
            double lngSin, lngCos;
            math.sincos(coor.Longitude / 180 * Math.PI, out lngSin, out lngCos);
            double4x4 ECEFToENURot = new double4x4(
                -lngSin,
                lngCos,
                0.0,
                0.0,
                -latSin * lngCos,
                -latSin * lngSin,
                latCos,
                0.0,
                latCos * lngCos,
                latCos * lngSin,
                latSin,
                0.0,
                0.0,
                0.0,
                0.0,
                1.0);

            _matrixStack.MultMatrix(
                MatrixStack.Translate(
                    new double3(-PositionInECEF.x, -PositionInECEF.y, -PositionInECEF.z)));
            _matrixStack.MultMatrix(ECEFToENURot);
            ECEFToENU = _matrixStack.GetMatrix();
            ENUToECEF = math.inverse(ECEFToENU);

            if (refPoint != null)
            {
                _geoRefLatitude = refPoint.Latitude;
                _geoRefLongitude = refPoint.Longitude;
                _geoRefHeight = refPoint.Height;
            }
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                return;
            }

            CalculateRealWorldPosition();
            EUN = new double3(transform.position.x, transform.position.y, transform.position.z);
            double3 ENU = new double3(EUN.x, EUN.z, EUN.y);
            EUS = new double3(EUN.x, EUN.y, -EUN.z);
            ECEF = MatrixStack.MultPoint(ENUToECEF, ENU);

            double3 llh = CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(
                ECEF
            );
            if (_oldPosition != transform.position)
            {
                shouldPosition = true;
                _oldPosition = transform.position;
            }
            if (_oldRotation != transform.rotation)
            {
                shouldPosition = true;
                _oldRotation = transform.rotation;
            }
            if (_oldScale != transform.localScale)
            {
                shouldPosition = true;
                _oldScale = transform.localScale;
            }

            if (shouldPosition)
            {
                _longitude = llh.x;
                _latitude = llh.y;
                _altitude = llh.z;
            }
        }

        private void OnEnable()
        {
            GeoReference = new GeoTilesReference(this.gameObject);
            CalculateRealWorldPosition();
        }

        private void OnDisable()
        {
            GeoReference = null;
        }
#else // !UNITY_EDITOR
        private void AddGeoAnchorAtRuntime()
        {
            IntPtr sessionHandle = ARCoreExtensions._instance.currentARCoreSessionHandle;
            // During boot this will return false a few times.
            if (sessionHandle == IntPtr.Zero)
            {
                return;
            }

            // Geospatial anchors cannot be created until the AR Session is stable and tracking:
            // https://developers.google.com/ar/develop/unity-arf/geospatial/geospatial-anchors#place_a_geospatial_anchor_in_the_real_world
            if (EarthApi.GetEarthTrackingState(sessionHandle) != TrackingState.Tracking)
            {
                Debug.Log("Waiting for AR Session to become stable (earthTrackingState != TrackingState.Tracking)");
                return;
            }

            // :TODO (b/278071434): Make the anchor manager a settable property
            ARAnchorManager anchorManager =
                ARCoreExtensions._instance.SessionOrigin.GetComponent<ARAnchorManager>();

            if (anchorManager == null)
            {
                Debug.LogError(
                    "The Session Origin has no Anchor Manager. " +
                    "Unable to place Geospatial Creator Anchor " +
                    name);
                _anchorResolution = AnchorResolutionState.Complete;
                return;
            }

            _anchorResolution = AnchorResolutionState.InProgress;
            switch (this.AltType)
            {
                case AltitudeType.Terrain:
                    StartCoroutine(ResolveTerrainAnchor(anchorManager));
                    break;
                case AltitudeType.Rooftop:
                    StartCoroutine(ResolveRooftopAnchor(anchorManager));
                    break;
                case AltitudeType.ManualAltitude:
                    // Manual altitude anchors don't use async APIs, so there's no coroutine
                    ResolveManualAltitudeAnchor(anchorManager);
                    break;
            }
        }

        private void FinishAnchor(ARGeospatialAnchor resolvedAnchor)
        {
            if (resolvedAnchor == null)
            {
                Debug.LogError("Failed to make Geospatial Anchor for " + name);
                // If we failed once, resolution is likley to keep failing, so don't retry endlessly
                _anchorResolution = AnchorResolutionState.Complete;
                return;
            }

            // Maintain an association between the ARGeospatialCreatorAnchor and the resolved
            // ARGeospatialAnchor by making the creator anchor a child of the runtime anchor.
            // We zero out the pose & rotation on the creator anchor, since the runtime
            // anchor will handle that from now on.
            transform.position = new Vector3(0, 0, 0);
            transform.rotation = Quaternion.identity;
            transform.SetParent(resolvedAnchor.transform, false);

            _anchorResolution = AnchorResolutionState.Complete;
            Debug.Log("Geospatial Anchor resolved: " + name);
        }

        private void ResolveManualAltitudeAnchor(ARAnchorManager anchorManager)
        {
            ARGeospatialAnchor anchor = anchorManager.AddAnchor(
                    Latitude, Longitude, Altitude, transform.rotation);
            FinishAnchor(anchor);
        }

        private IEnumerator ResolveTerrainAnchor(ARAnchorManager anchorManager)
        {
            ARGeospatialAnchor anchor = null;

            ResolveAnchorOnTerrainPromise promise =
                        anchorManager.ResolveAnchorOnTerrainAsync(
                            Latitude, Longitude, AltitudeOffset, transform.rotation);

            yield return promise;
            var result = promise.Result;
            if (result.TerrainAnchorState == TerrainAnchorState.Success)
            {
                anchor = result.Anchor;
            }
            FinishAnchor(anchor);
            yield break;
        }

        private IEnumerator ResolveRooftopAnchor(ARAnchorManager anchorManager)
        {
            ARGeospatialAnchor anchor = null;

            ResolveAnchorOnRooftopPromise promise =
                        anchorManager.ResolveAnchorOnRooftopAsync(
                            Latitude, Longitude, AltitudeOffset, transform.rotation);

            yield return promise;
            var result = promise.Result;
            if (result.RooftopAnchorState == RooftopAnchorState.Success)
            {
                anchor = result.Anchor;
            }
            FinishAnchor(anchor);
            yield break;
        }

        private void Update()
        {
            // Only create the geospatial anchor in Player mode
            if (!Application.isPlaying)
            {
                return;
            }

            // Only attempt to create the geospatial anchor once
            if (_anchorResolution == AnchorResolutionState.NotStarted)
            {
                AddGeoAnchorAtRuntime();
            }
        }
#endif // UNITY_EDITOR
    }
}
#endif // UNITY_X_OR_NEWER
