using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FluffyUnderware.Curvy;
using sys = System;

namespace LandscapeGenerator
{

    public class LandscapeObject
    {
        public List<CurvySpline> _paths;
        public List<GameObject> _path_objects;

        public Vector3 _position;

        public GameObject _main_landscape_object;
        public GameObject _landscape_object;

        public string _name = "unnamedObject";
        public float[] _landscape_parameters;

        public LandscapeObject(Vector3 position, GameObject main_landscape_object)
        {
            _position = position;
            _paths = new List<CurvySpline>();
            _path_objects = new List<GameObject>();
            set_settings();

            _main_landscape_object = main_landscape_object;
            _landscape_object = new GameObject(_name);
            _landscape_object.transform.SetParent(_main_landscape_object.transform);
            _landscape_object.transform.position = _position;

            generate_Random();
        }

        public virtual void set_settings()
        {

        }

        public virtual void generate_Random()
        {

        }

        public static Vector3 generate_random_vector3(float seed)
        {
            return new Vector3(generate_random_number(seed), generate_random_number(seed + 1), generate_random_number(seed + 2));
        }

        public static float generate_random_number(float seed)
        {
            float wert = (Mathf.Sin(seed * 78.233f + 745.23f) * 43758.5453f);
            return wert - Mathf.Floor(wert);
        }
    }

    class Mountain : LandscapeObject
    {
        public Mountain(Vector3 position, GameObject main_landscape_object) : base(position, main_landscape_object) { }

        public override void set_settings()
        {
            _name = "Mountain";
            _landscape_parameters = new float[] { 25, 40, 6, 5, 0.5f, 1f };
        }

        public override void generate_Random()
        {
            float height = _landscape_parameters[0];
            float radius = _landscape_parameters[1];
            int number_of_arms = sys.Convert.ToInt32(_landscape_parameters[2]);
            int number_of_segments = sys.Convert.ToInt32(_landscape_parameters[3]);
            float arm_randomness = _landscape_parameters[4];

            sys.Random rnd = new sys.Random();

            float master_seed = _landscape_parameters[5];

            _paths = new List<CurvySpline>();
            _path_objects = new List<GameObject>();

            Vector3 Midpoint = new Vector3(0, height, 0);
            for (int i = 0; i < number_of_arms; i++)
            {
                List<Vector3> points = new List<Vector3>();
                List<Vector3> sub_arm_points = new List<Vector3>();

                float random_arm_offset = sys.Convert.ToSingle(rnd.NextDouble() * arm_randomness);
                float random_radius_offset = generate_random_number(i + master_seed) * 1f + 0.6f;
                float random_downpull_strength = generate_random_number(i + master_seed) * 3;

                Vector3 Endpoint = new Vector3(Mathf.Cos((i + random_arm_offset) * 2 * Mathf.PI / number_of_arms + 1) * radius * random_radius_offset, _position.y, Mathf.Sin((i + random_arm_offset) * 2 * Mathf.PI / number_of_arms + 1) * radius * random_radius_offset);

                for (int j = 0; j < number_of_segments; j++)
                {
                    float x = (j + 1f) / number_of_segments;
                    points.Add((Midpoint * x + Endpoint * (1 - x)) + generate_random_vector3(i * j + master_seed) * 3 - new Vector3(0, -x * x + x, 0) * 17 * random_downpull_strength);
                }

                Vector3 Sub_Arm_Endpoint = new Vector3(Mathf.Cos((i + 0.5f + random_arm_offset) * 2 * Mathf.PI / number_of_arms + 1) * radius * random_radius_offset, _position.y, Mathf.Sin((i + 0.5f + random_arm_offset) * 2 * Mathf.PI / number_of_arms + 1) * radius * random_radius_offset);

                for (int j = 0; j < number_of_segments; j++)
                {
                    float x = (j + 1f) / number_of_segments;
                    sub_arm_points.Add((Midpoint * x + Sub_Arm_Endpoint * (1 - x)) + generate_random_vector3(i * j + master_seed) * 3 - new Vector3(0, -x * x + x, 0) * 30 * random_downpull_strength);
                }

                CurvySpline current_arm = CurvySpline.Create();
                foreach (Vector3 point in points)
                {
                    current_arm.Add(point);
                }

                CurvySpline current_sub_arm = CurvySpline.Create();
                foreach (Vector3 point in sub_arm_points)
                {
                    current_sub_arm.Add(point);
                }
                _paths.Add(current_sub_arm);
                _paths.Add(current_arm);

                current_arm.Interpolation = CurvyInterpolation.Bezier;
                current_sub_arm.Interpolation = CurvyInterpolation.Bezier;

                current_arm.transform.SetParent(_landscape_object.transform, false);
                current_sub_arm.transform.SetParent(_landscape_object.transform, false);
            }
        }
    }

    class MountainRange : LandscapeObject
    {
        List<Mountain> _Mountains = new List<Mountain>();

        CurvySpline _GuideSpline;

        public MountainRange(Vector3 position, GameObject main_landscape_object) : base(position, main_landscape_object)
        {

        }

        public override void set_settings()
        {
            _name = "Mountain Range";
            // Number of Mountains; Mountain offset Amount; height; radius; number of arms; number of segments; arm randomness;
            _landscape_parameters = new float[] { 5, 1f, 25, 40, 6, 5, 0.5f, 1f };

            _GuideSpline = CurvySpline.Create();
            _GuideSpline.Add(new Vector3(0, 10, 0));
            _GuideSpline.Add(new Vector3(100, 10, 0));
        }

        public override void generate_Random()
        {
            _paths = new List<CurvySpline>();
            _Mountains = new List<Mountain>();

            float mountainNumber = _landscape_parameters[0];
            float mountainHeight = _landscape_parameters[3];
            BezierSpline spline = new BezierSpline(_GuideSpline);

            for (int i = 0; i < mountainNumber; i++)
            {
                float t = i * spline.m_Number_Segments / mountainNumber;
                Vector3 moutain_position = spline.Bezier(t);
                moutain_position.y -= mountainHeight;

                //Debug.Log(moutain_position);
                Mountain currentMountain = new Mountain(moutain_position, _main_landscape_object);
                _Mountains.Add(currentMountain);
                currentMountain._landscape_parameters[5] = i;
                currentMountain.generate_Random();


                _paths.AddRange(currentMountain._paths);
            }
        }

        public void DeleteSplines()
        {
            
            for (int j = 0; j < _paths.Count; j++)
            {
                GameObject.Destroy(_paths[j]);
            }
            
            for (int i = 0; i < _Mountains.Count; i++)
            {
                for (int j = 0; j < _Mountains[i]._paths.Count; j++)
                {
                    GameObject.Destroy(_Mountains[i]._paths[j]);
                }
            }
        }
    }
}