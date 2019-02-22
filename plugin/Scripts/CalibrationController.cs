﻿using System;
using System.Collections.Generic;
using UnityEngine;

namespace PupilLabs
{
    public class CalibrationController : MonoBehaviour
    {
        public SubscriptionsController subsCtrl;
        public new Camera camera;
        public Transform marker;

        public CalibrationSettings settings;
        public CalibrationTargets targets;

        //events
        public delegate void CalibrationEndedDel();
        public event CalibrationEndedDel OnCalibrationFailed;
        public event CalibrationEndedDel OnCalibrationSucceeded;

        //members
        Calibration calibration = new Calibration();

        int currentCalibrationPoint;
        int currentCalibrationSamples;
        Vector3 currLocalTargetPos;

        float tLastSample = 0;
        float tLastTarget = 0;

        void OnEnable()
        {
            calibration.OnCalibrationSucceeded += CalibrationSucceeded;
            calibration.OnCalibrationFailed += CalibrationFailed;
        }

        void OnDisable()
        {
            calibration.OnCalibrationSucceeded -= CalibrationSucceeded;
            calibration.OnCalibrationFailed -= CalibrationFailed;
        }

        void Update()
        {
            if (calibration.IsCalibrating)
            {
                UpdateCalibration();
            }

            if (subsCtrl.IsConnected && Input.GetKeyUp(KeyCode.C)) //TODO needs some public API instead of keypress only
            {
                if (calibration.IsCalibrating)
                {
                    calibration.StopCalibration();
                }
                else
                {
                    InitializeCalibration();
                }
            }
        }

        private void InitializeCalibration()
        {
            Debug.Log("Starting Calibration");

            currentCalibrationPoint = 0;
            currentCalibrationSamples = 0;

            UpdatePosition();

            marker.gameObject.SetActive(true);

            calibration.StartCalibration(settings, subsCtrl);
            Debug.Log($"Sample Rate: {settings.SampleRate}");
        }

        private void UpdateCalibration()
        {
            float tNow = Time.time;

            if (tNow - tLastSample >= 1f / settings.SampleRate - Time.deltaTime / 2f)
            {

                if (tNow - tLastTarget < settings.ignoreInitialSeconds - Time.deltaTime / 2f)
                {
                    return;
                }

                tLastSample = tNow;

                //Adding the calibration reference data to the list that wil;l be passed on, once the required sample amount is met.
                AddSample(tNow);

                currentCalibrationSamples++;//Increment the current calibration sample. (Default sample amount per calibration point is 120)

                if (currentCalibrationSamples >= settings.samplesPerTarget || tNow - tLastTarget >= settings.secondsPerTarget)
                {
                    // Debug.Log($"update target. last duration = {tNow - tLastTarget} samples = {currentCalibrationSamples}");

                    calibration.SendCalibrationReferenceData(); //including clear!
                    
                    //NEXT TARGET
                    if (currentCalibrationPoint < targets.GetTargetCount())
                    {
                        
                        currentCalibrationSamples = 0;

                        UpdatePosition();

                    }
                    else
                    {
                        calibration.StopCalibration();
                    }
                }
            }
        }

        private void CalibrationSucceeded()
        {
            CalibrationEnded();

            if (OnCalibrationSucceeded != null)
            {
                OnCalibrationSucceeded();
            }
        }

        private void CalibrationFailed()
        {
            CalibrationEnded();

            if (OnCalibrationFailed != null)
            {
                OnCalibrationFailed();
            }
        }

        private void CalibrationEnded()
        {
            marker.gameObject.SetActive(false);
        }

        private void AddSample(float time)
        {
            float[] refData;
            
            if (settings.mode == CalibrationSettings.Mode._3D)
            {
                refData = new float[]{currLocalTargetPos.x,currLocalTargetPos.y,currLocalTargetPos.z};
                refData[1] /= camera.aspect; //TODO TBD why?
                
                for (int i = 0; i < refData.Length; i++)
                {
                    refData[i] *= Helpers.PupilUnitScalingFactor;
                }
            }
            else
            {
                Vector3 worldPos = camera.transform.localToWorldMatrix.MultiplyPoint(currLocalTargetPos);
                Vector3 viewportPos = camera.WorldToViewportPoint(worldPos);
                refData = new float[]{viewportPos.x,viewportPos.y};
            }

            calibration.AddCalibrationPointReferencePosition(refData,time);
        }

        private void UpdatePosition()
        {
            currLocalTargetPos = targets.GetLocalTargetPosAt(currentCalibrationPoint);
            
            marker.position = camera.transform.localToWorldMatrix.MultiplyPoint(currLocalTargetPos);
                        
            currentCalibrationPoint++;
            tLastTarget = Time.time;
        }
    }
}