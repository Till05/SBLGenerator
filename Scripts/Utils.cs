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
        public List<Vector3[]> m_DiscPoints = new List<Vector3[]>();

        public int m_Number_Segments
        {
            get
            {
                return (m_Points.Count - 4) / 3 + 1;
            }
        }

        public BezierSpline() { }

        public BezierSpline(List<Vector3> points)
        {
            m_Points = points;
        }

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

        public Vector3 Bezier(float g)
        {
            int i = Mathf.FloorToInt(g);
            float t = g - i;

            Vector3 point1 = m_Points[i * 3 + 0];
            Vector3 point2 = m_Points[i * 3 + 1];
            Vector3 point3 = m_Points[i * 3 + 2];
            Vector3 point4 = m_Points[i * 3 + 3];

            return (-point1 + 3 * point2 - 3 * point3 + point4) * t * t * t +
                   (3 * point1 - 6 * point2 + 3 * point3) * t * t +
                   (-3 * point1 + 3 * point2) * t +
                   point1;
        }

        public List<BezierSpline> splice_to_curves()
        {
            List<BezierSpline> curves = new List<BezierSpline>();

            for (int j = 0; j < (m_Points.Count - 4) / 3 + 1; j++)
            {
                Vector3 point0 = m_Points[0 + 3 * j];
                Vector3 point1 = m_Points[1 + 3 * j];
                Vector3 point2 = m_Points[2 + 3 * j];
                Vector3 point3 = m_Points[3 + 3 * j];

                curves.Add(new BezierSpline(new List<Vector3> { point0, point1, point2, point3 }));

                curves[j].m_BoundingBoxes.Add(m_BoundingBoxes[j]);
                curves[j].m_DiscPoints.Add(m_DiscPoints[j]);
                curves[j].m_Coefficients.Add(m_Coefficients[j]);
            }

            return curves;
        }

        public void drawBounds()
        {
            Debug.DrawLine(new Vector3(m_BoundingBoxes[0][0].x, 0, m_BoundingBoxes[0][0].y), new Vector3(m_BoundingBoxes[0][1].x, 0, m_BoundingBoxes[0][1].y), Color.red, 30);
            Debug.DrawLine(new Vector3(m_BoundingBoxes[0][2].x, 0, m_BoundingBoxes[0][2].y), new Vector3(m_BoundingBoxes[0][3].x, 0, m_BoundingBoxes[0][3].y), Color.red, 30);
            Debug.DrawLine(new Vector3(m_BoundingBoxes[0][0].x, 0, m_BoundingBoxes[0][0].y), new Vector3(m_BoundingBoxes[0][2].x, 0, m_BoundingBoxes[0][2].y), Color.red, 30);
            Debug.DrawLine(new Vector3(m_BoundingBoxes[0][1].x, 0, m_BoundingBoxes[0][1].y), new Vector3(m_BoundingBoxes[0][3].x, 0, m_BoundingBoxes[0][3].y), Color.red, 30);
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

        public void doPrecalcs(int discInts)
        {
            for (int j = 0; j < (m_Points.Count - 4) / 3 + 1; j++)
            {
                Vector2 point0 = new Vector2(m_Points[0 + 3 * j].x, m_Points[0 + 3 * j].z);
                Vector2 point1 = new Vector2(m_Points[1 + 3 * j].x, m_Points[1 + 3 * j].z);
                Vector2 point2 = new Vector2(m_Points[2 + 3 * j].x, m_Points[2 + 3 * j].z);
                Vector2 point3 = new Vector2(m_Points[3 + 3 * j].x, m_Points[3 + 3 * j].z);

                Vector3[] pointsArray = new Vector3[discInts];
                for (int i = 0; i < discInts; i++)
                {
                    pointsArray[i] = Bezier(point0, point1, point2, point3, i / (discInts - 1f));
                }
                m_DiscPoints.Add(pointsArray);

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
                //Color color = UnityEngine.Random.ColorHSV();
                //Debug.DrawLine(new Vector3(Bounds[0].x, 0, Bounds[0].y), new Vector3(Bounds[1].x, 0, Bounds[1].y), color, 30);
                //Debug.DrawLine(new Vector3(Bounds[2].x, 0, Bounds[2].y), new Vector3(Bounds[3].x, 0, Bounds[3].y), color, 30);
                //Debug.DrawLine(new Vector3(Bounds[0].x, 0, Bounds[0].y), new Vector3(Bounds[2].x, 0, Bounds[2].y), color, 30);
                //Debug.DrawLine(new Vector3(Bounds[1].x, 0, Bounds[1].y), new Vector3(Bounds[3].x, 0, Bounds[3].y), color, 30);
                m_BoundingBoxes.Add(Bounds);
                m_Coefficients.Add(coefficients_array);
            }
        }
    }

    public struct IntersectionData
    {
        public bool hit;
        public bool hitOpp;

        public float distance;
        public float distanceOpp;

        public float height;
        public float heightOpp;
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
        public int discInts;
        public int relaxtIts;
    }

    class IntersectionTests
    {
        public static bool test_Intersection_Quads(Vector2[] Bounds0, Vector2[] Bounds1)
        {
            foreach (Vector2 point in Bounds1)
            {
                if (!(point.x > Bounds0[0].x || point.y > Bounds0[0].y || point.x < Bounds0[3].x || point.y < Bounds0[3].y))
                {
                    return true;
                }
            }

            foreach (Vector2 point in Bounds0)
            {
                if (!(point.x > Bounds1[0].x || point.y > Bounds1[0].y || point.x < Bounds1[3].x || point.y < Bounds1[3].y))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool test_Intersection_Quad_Gerade(Vector2[] Bounds, Vector2 position, Vector2 normal)
        {
            float dot1 = Vector2.Dot(Bounds[0] - position, normal);
            float dot2 = Vector2.Dot(Bounds[1] - position, normal);
            float dot3 = Vector2.Dot(Bounds[2] - position, normal);
            float dot4 = Vector2.Dot(Bounds[3] - position, normal);

            float dot1sign = MathF.Sign(dot1);

            return !(dot1sign == MathF.Sign(dot2) && dot1sign == MathF.Sign(dot3) && dot1sign == MathF.Sign(dot4));
        }

        public static IntersectionData test_Intersection_Curve_Gerade(BezierSpline spline, Vector2 position, Vector2 direction)
        {
            IntersectionData bestData;
            bestData.hit = false;
            bestData.hitOpp = false;
            bestData.distance = 1000000;
            bestData.distanceOpp = 1000000;
            bestData.height = 0;
            bestData.heightOpp = 0;


            Vector2 PointA = position;
            Vector2 PointB = position + direction;
            List<Vector2[]> coefficients = spline.m_Coefficients;
            List<Vector2[]> Bounds = spline.m_BoundingBoxes;
            List<Vector3> points = spline.m_Points;

            Vector2 AminusB = direction;
            Vector2 normal = new Vector2(AminusB.y, -AminusB.x);

            num.Complex[] t = new num.Complex[3];

            for (int i = 0; i < (points.Count - 4) / 3 + 1; i++)
            {
                Vector2[] bounds = Bounds[i];

                float dot1 = Vector2.Dot(bounds[0] - PointA, normal);
                float dot2 = Vector2.Dot(bounds[1] - PointA, normal);
                float dot3 = Vector2.Dot(bounds[2] - PointA, normal);
                float dot4 = Vector2.Dot(bounds[3] - PointA, normal);

                float dot1sign = MathF.Sign(dot1);

                if (!(dot1sign == MathF.Sign(dot2) && dot1sign == MathF.Sign(dot3) && dot1sign == MathF.Sign(dot4)))
                {
                    Vector2 point0 = new Vector2(points[0 + 3 * i].x, points[0 + 3 * i].z);
                    Vector2 point1 = new Vector2(points[1 + 3 * i].x, points[1 + 3 * i].z);
                    Vector2 point2 = new Vector2(points[2 + 3 * i].x, points[2 + 3 * i].z);
                    Vector2 point3 = new Vector2(points[3 + 3 * i].x, points[3 + 3 * i].z);

                    Vector2 a = coefficients[i][0];
                    Vector2 b = coefficients[i][1];
                    Vector2 c = coefficients[i][2];
                    Vector2 d = coefficients[i][3];

                    float af = a.y * AminusB.x - a.x * AminusB.y;
                    float bf = b.y * AminusB.x - b.x * AminusB.y;
                    float cf = c.y * AminusB.x - c.x * AminusB.y;
                    float df = (d.y - PointA.y) * AminusB.x - (d.x - PointA.x) * AminusB.y;

                    float p = (bf * bf - 3 * af * cf);
                    float q = (2 * bf * bf * bf - 9 * af * bf * cf + 27 * af * af * df);

                    num.Complex C = num.Complex.Pow((q + num.Complex.Sqrt(q * q - 4 * p * p * p)) / 2, 1f / 3f);

                    num.Complex n = (-1 + num.Complex.Sqrt(-3)) / 2;

                    t[0] = -1 / (3 * af) * (bf + C + p / C);
                    t[1] = -1 / (3 * af) * (bf + n * C + p / (n * C));
                    t[2] = -1 / (3 * af) * (bf + n * n * C + p / (n * n * C));

                    for (int j = 0; j < 3; j++)
                    {
                        if (t[j].Imaginary < 0.0001 && t[j].Imaginary > -0.00001)
                        {
                            float curr_t = Convert.ToSingle(t[j].Real);

                            if (curr_t > 0 && curr_t < 1)
                            {
                                Vector3 bezier_point = BezierSpline.Bezier(points[0 + 3 * i], points[1 + 3 * i], points[2 + 3 * i], points[3 + 3 * i], curr_t);
                                float possible_point_sign = (a.x * curr_t * curr_t * curr_t + b.x * curr_t * curr_t + c.x * curr_t + d.x - PointA.x) / (PointB.x - PointA.x);

                                if (possible_point_sign > 0)
                                {
                                    float distance = (new Vector2(bezier_point.x, bezier_point.z) - PointA).magnitude;
                                    float height = bezier_point.y;
                                    if (bestData.distance > distance)
                                    {
                                        bestData.distance = distance;
                                        bestData.height = height;
                                        bestData.hit = true;
                                    }
                                }
                                if (possible_point_sign < 0)
                                {
                                    float distance = (new Vector2(bezier_point.x, bezier_point.z) - PointA).magnitude;
                                    float height = bezier_point.y;
                                    if (bestData.distanceOpp > distance)
                                    {
                                        bestData.distanceOpp = distance;
                                        bestData.heightOpp = height;
                                        bestData.hitOpp = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return bestData;
        }
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
