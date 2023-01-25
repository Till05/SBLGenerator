using num = System.Numerics;
using UnityEngine;
using System.Collections.Generic;
using System;
using FluffyUnderware.Curvy;

namespace LandscapeGenerator
{
    public class BezierSpline
    {
        public List<Vector3> m_Points = new List<Vector3>();
        public List<Vector2[]> m_Coefficients = new List<Vector2[]>();
        public List<Vector2[]> m_BoundingBoxes = new List<Vector2[]>();

        public BezierSpline() { }

        public BezierSpline(CurvySpline Spline)
        {
            m_Points.Add(Spline.ControlPointsList[0].transform.position);
            m_Points.Add(Spline.ControlPointsList[0].HandleOutPosition);

            for (int j = 1; j < Spline.ControlPointsList.Count - 1; j++)
            {
                m_Points.Add(Spline.ControlPointsList[j].HandleInPosition);
                m_Points.Add(Spline.ControlPointsList[j].transform.position);
                m_Points.Add(Spline.ControlPointsList[j].HandleOutPosition);
            }

            m_Points.Add(Spline.ControlPointsList[Spline.ControlPointsList.Count - 1].HandleInPosition);
            m_Points.Add(Spline.ControlPointsList[Spline.ControlPointsList.Count - 1].transform.position);

            if (Spline.Closed == true)
            {
                m_Points.Add(Spline.ControlPointsList[Spline.ControlPointsList.Count - 1].HandleOutPosition);
                m_Points.Add(Spline.ControlPointsList[0].HandleInPosition);
                m_Points.Add(Spline.ControlPointsList[0].transform.position);
            }


        }

        public static Vector3 Bezier(Vector3 point1, Vector3 point2, Vector3 point3, Vector3 point4, float t)
        {
            return (-point1 + 3 * point2 - 3 * point3 + point4) * t * t * t +
                   (3 * point1 - 6 * point2 + 3 * point3) * t * t +
                   (-3 * point1 + 3 * point2) * t +
                   point1;
        }

        public static Vector2 Bezier(Vector2 point1, Vector2 point2, Vector2 point3, Vector2 point4, float t)
        {
            return (-point1 + 3 * point2 - 3 * point3 + point4) * t * t * t +
                   (3 * point1 - 6 * point2 + 3 * point3) * t * t +
                   (-3 * point1 + 3 * point2) * t +
                   point1;
        }

        public void doPrecalcs()
        {
            for (int j = 0; j < (m_Points.Count - 4) / 3 + 1; j++)
            {
                Vector2 point0 = new Vector2(m_Points[0 + 3 * j].x, m_Points[0 + 3 * j].z);
                Vector2 point1 = new Vector2(m_Points[1 + 3 * j].x, m_Points[1 + 3 * j].z);
                Vector2 point2 = new Vector2(m_Points[2 + 3 * j].x, m_Points[2 + 3 * j].z);
                Vector2 point3 = new Vector2(m_Points[3 + 3 * j].x, m_Points[3 + 3 * j].z);

                Vector2[] coefficients_array = new Vector2[4];
                coefficients_array[0] = -point0 + (3 * point1) - (3 * point2) + point3;
                coefficients_array[1] = (3 * point0) - (6 * point1) + (3 * point2);
                coefficients_array[2] = (-3 * point0) + (3 * point1);
                coefficients_array[3] = point0;

                Vector2 a = -3 * point0 + 9 * point1 - 9 * point2 + 3 * point3;
                Vector2 b = 6 * point0 - 12 * point1 + 6 * point2;
                Vector2 c = -3 * point0 + 3 * point1;

                float t1x = (-b.x + MathF.Sqrt(b.x * b.x - 4 * a.x * c.x)) / (2 * a.x);
                float t2x = (-b.x - MathF.Sqrt(b.x * b.x - 4 * a.x * c.x)) / (2 * a.x);
                float t1y = (-b.y + MathF.Sqrt(b.y * b.y - 4 * a.y * c.y)) / (2 * a.y);
                float t2y = (-b.y - MathF.Sqrt(b.y * b.y - 4 * a.y * c.y)) / (2 * a.y);

                List<Vector2> possible_points = new List<Vector2>();

                possible_points.Add(point0);
                possible_points.Add(point3);

                if (t1x < 1 && t1x > 0) possible_points.Add(Bezier(point0, point1, point2, point3, t1x));
                if (t2x < 1 && t2x > 0) possible_points.Add(Bezier(point0, point1, point2, point3, t2x));
                if (t1y < 1 && t1y > 0) possible_points.Add(Bezier(point0, point1, point2, point3, t1y));
                if (t2y < 1 && t2y > 0) possible_points.Add(Bezier(point0, point1, point2, point3, t2y));

                float x_most_up = -100000000;
                float x_most_down = 100000000;
                float y_most_up = -100000000;
                float y_most_down = 100000000;

                foreach (Vector2 item in possible_points)
                {
                    //                    Debug.Log("item: " + item);
                    if (item.x > x_most_up)
                    {
                        x_most_up = item.x;
                    }
                    if (item.x < x_most_down)
                    {
                        x_most_down = item.x;
                    }
                    if (item.y > y_most_up)
                    {
                        y_most_up = item.y;
                    }
                    if (item.y < y_most_down)
                    {
                        y_most_down = item.y;
                    }
                }

                Vector2[] Bounds = new Vector2[] { new Vector2(x_most_up, y_most_up), new Vector2(x_most_down, y_most_up), new Vector2(x_most_up, y_most_down), new Vector2(x_most_down, y_most_down) };
                m_BoundingBoxes.Add(Bounds);
                m_Coefficients.Add(coefficients_array);
            }
        }
    }

    public struct GenSettings
    {
        /*
        public GenSettings() 
        { 
            resAmplifier = 2;
            persistence = 0.5f;
            lacunarity = 0.5f;
            numOctaves = 4;
            scale = 5;
            noise_strength = 0.5f;
            sampels_number = 10;
        }*/

        public int resAmplifier;
        public float persistence;
        public float lacunarity;
        public int numOctaves;
        public float scale;
        public float noise_strength;
        public int sampels_number;
    }

    public class SimulationSettings
    {
        [Range(0f, 10f)]
        public float TimeScale = 0f;

        public float PipeLength = 1;//1f / 256;
        public Vector2 CellSize = new Vector2(1, 1);//new Vector2(1f / 256, 1f / 256);//new Vector2(1f / 256, 1f / 256);

        [Range(0, 0.5f)]
        public float RainRate = 0.003f;

        [Range(0, 1f)]
        public float Evaporation = 0.2f;

        [Range(0.001f, 1000)]
        public float PipeArea = 5f;

        [Range(0.1f, 20f)]
        public float Gravity = 9.81f;

        [Header("Hydraulic erosion")]
        [Range(0.1f, 3f)]
        public float SedimentCapacity = 100f;

        [Range(0.1f, 2f)]
        public float SoilSuspensionRate = 0.8f;

        [Range(0.1f, 3f)]
        public float SedimentDepositionRate = 0.8f;

        [Range(0f, 10f)]
        public float SedimentSofteningRate = 5f; // 5f

        [Range(0f, 40f)]
        public float MaximalErosionDepth = 10f;

        [Header("Thermal erosion")]
        [Range(0, 1000f)]
        public float ThermalErosionTimeScale = 20f;

        [Range(0, 1f)]
        public float ThermalErosionRate = 1f; // 0.15f

        [Range(0f, 1f)]
        public float TalusAngleTangentCoeff = 0.6f;

        [Range(0f, 1f)]
        public float TalusAngleTangentBias = 0.2f;

    }

    class Noise
    {
        public static float fbm(int iterationen, Vector2 coord, float persistence, float lacunarity)
        {
            float height = 0;
            float amplitude = 0.5f;
            coord = coord * 5;

            for (int i = 1; i < iterationen + 2; i++)
            {
                coord.x += i;
                coord.y += i;
                height += perlin(coord) * amplitude;
                amplitude *= persistence;
                coord = coord * lacunarity;
            }
            return height - 0.5f;
        }

        public static float rand(Vector2 seed)
        {
            double x = MathF.Sin(Vector2.Dot(seed, new Vector2(12.9898f, 78.233f))) * 43758.5453;
            return (float)x - (int)x;
        }

        public static float perlin(Vector2 coord)
        {
            Vector2 i = new Vector2(MathF.Floor(coord.x), MathF.Floor(coord.y));
            Vector2 f = coord - i;

            float tl = rand(i) * 6.283f;
            float tr = rand(i + new Vector2(1f, 0f)) * 6.283f;
            float bl = rand(i + new Vector2(0f, 1f)) * 6.283f;
            float br = rand(i + new Vector2(1f, 1f)) * 6.283f;

            Vector2 tlvec = new Vector2(-MathF.Sin(tl), MathF.Cos(tl));
            Vector2 trvec = new Vector2(-MathF.Sin(tr), MathF.Cos(tr));
            Vector2 blvec = new Vector2(-MathF.Sin(bl), MathF.Cos(bl));
            Vector2 brvec = new Vector2(-MathF.Sin(br), MathF.Cos(br));

            float tldot = Vector2.Dot(tlvec, f);
            float trdot = Vector2.Dot(trvec, f - new Vector2(1f, 0f));
            float bldot = Vector2.Dot(blvec, f - new Vector2(0f, 1f));
            float brdot = Vector2.Dot(brvec, f - new Vector2(1f, 1f));

            Vector2 cubic = f * f * (new Vector2(3f, 3f) - new Vector2(2f, 2f) * f);

            return ((1 - cubic.y) * tldot + cubic.y * bldot) * (1 - cubic.x) + ((1 - cubic.y) * trdot + cubic.y * brdot) * cubic.x + 0.5f;
        }

        public static int mod(int x, int m)
        {
            return (x % m + m) % m;
        }
    }
}
