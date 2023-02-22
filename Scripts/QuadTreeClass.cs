using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.Profiling;

namespace LandscapeGenerator
{
    public class QuadTree
    {
        List<BezierSpline> All_Splines = new List<BezierSpline>();

        public QuadTree() { }

        Quad mainQuad;

        //für jedes getroffene Quad werden die Splines und die Position des Quads zur liste hinzugefügt
        public List<(List<BezierSpline>, float)> querrySplines = new List<(List<BezierSpline>, float)>();
        public List<(List<BezierSpline>, float)> querrySplinesOpp = new List<(List<BezierSpline>, float)>();

        public void constructTree(List<BezierSpline> splines, int max_splines_per_square)
        {
            foreach (BezierSpline spline in splines)
            {
                All_Splines.AddRange(spline.splice_to_curves());
            }
            //Debug.Log(splines.Count);
            //Debug.Log(All_Splines.Count);

            float max_up_x = -1000000000;
            float max_down_x = 1000000000;
            float max_up_y = -1000000000;
            float max_down_y = 1000000000;

            //Größe des Baumes bestimmen
            for (int i = 0; i < All_Splines.Count; i++)
            {
                for (int j = 0; j < All_Splines[i].m_BoundingBoxes.Count; j++)
                {
                    Vector2 up_left = All_Splines[i].m_BoundingBoxes[j][0];
                    Vector2 down_right = All_Splines[i].m_BoundingBoxes[j][3];

                    if (up_left.x > max_up_x)
                    {
                        max_up_x = up_left.x;
                    }
                    if (up_left.y > max_up_y)
                    {
                        max_up_y = up_left.y;
                    }
                    if (down_right.x < max_down_x)
                    {
                        max_down_x = down_right.x;
                    }
                    if (down_right.y < max_down_y)
                    {
                        max_down_y = down_right.y;
                    }
                }
            }

            Vector2[] Bounds = new Vector2[] { new Vector2(max_up_x, max_up_y), new Vector2(max_down_x, max_up_y), new Vector2(max_up_x, max_down_y), new Vector2(max_down_x, max_down_y) };

            Debug.DrawLine(new Vector3(Bounds[0].x, 0, Bounds[0].y), new Vector3(Bounds[1].x, 0, Bounds[1].y), Color.red, 30);
            Debug.DrawLine(new Vector3(Bounds[2].x, 0, Bounds[2].y), new Vector3(Bounds[3].x, 0, Bounds[3].y), Color.red, 30);
            Debug.DrawLine(new Vector3(Bounds[0].x, 0, Bounds[0].y), new Vector3(Bounds[2].x, 0, Bounds[2].y), Color.red, 30);
            Debug.DrawLine(new Vector3(Bounds[1].x, 0, Bounds[1].y), new Vector3(Bounds[3].x, 0, Bounds[3].y), Color.red, 30);

            mainQuad = new Quad(Bounds, All_Splines, max_splines_per_square, this);
        }

        //erster float: höhe, zweiter float: distanz, höhe in gegenrichtung, distanz in gegenrichutung
        public (float, float, float, float) traceTree(Vector2 position, Vector2 direction)
        {
            querrySplines.Clear();
            querrySplinesOpp.Clear();

            Vector2 normal = new Vector2(direction.y, -direction.x);
            // get list of possible Splines
            // if Quad has relevant Splines->puts splines in querrySplines, lists get sorted by distance 
            // according Quad posions get put in querryPositions
            //Profiler.BeginSample("Trace Main Quad");
            mainQuad.traceQuad(position, normal, direction);
            //Profiler.EndSample();

            // calculate intersections with splines
            //Profiler.BeginSample("Sort");
            querrySplines.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            querrySplinesOpp.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            //Profiler.EndSample();

            //Debug.Log(querrySplines.Count);
            //Debug.Log(querrySplinesOpp.Count);

            //deal with the Quad the position is inside
            (List<BezierSpline>, float) closestQuad;
            if (querrySplines[0].Item2 < querrySplinesOpp[0].Item2)
            {
                closestQuad = querrySplines[0];
                querrySplines.RemoveAt(0);
            }
            else
            {
                closestQuad = querrySplinesOpp[0];
                querrySplinesOpp.RemoveAt(0);
            }

            IntersectionData bestData;
            bestData.hit = false;
            bestData.hitOpp = false;
            bestData.distance = 100000000;
            bestData.distanceOpp = 100000000;
            bestData.height = 0;
            bestData.heightOpp = 0;

            for (int i = 0; i < closestQuad.Item1.Count; i++)
            {
                IntersectionData intersectionData = IntersectionTests.test_Intersection_Curve_Gerade(closestQuad.Item1[i], position, direction);
                if (intersectionData.hit)
                {
                    bestData.hit = true;
                    if (intersectionData.distance < bestData.distance)
                    {
                        bestData.distance = intersectionData.distance;
                        bestData.height = intersectionData.height;
                    }
                }
                if (intersectionData.hitOpp)
                {
                    if (intersectionData.distanceOpp < bestData.distanceOpp)
                    {
                        bestData.distanceOpp = intersectionData.distanceOpp;
                        bestData.heightOpp = intersectionData.heightOpp;
                    }
                }
            }

            // deal with the rest
            if (!bestData.hit)
            {
                for (int i = 0; i < querrySplines.Count; i++)
                {
                    closestQuad = querrySplines[i];
                    for (int j = 0; j < closestQuad.Item1.Count; j++)
                    {
                        IntersectionData intersectionData = IntersectionTests.test_Intersection_Curve_Gerade(closestQuad.Item1[j], position, direction);
                        if (intersectionData.hit)
                        {
                            bestData.hit = true;
                            if (intersectionData.distance < bestData.distance)
                            {
                                bestData.distance = intersectionData.distance;
                                bestData.height = intersectionData.height;
                            }
                        }
                    }
                    if (bestData.hit) break;
                }
            }
            if (!bestData.hitOpp)
            {
                for (int i = 0; i < querrySplinesOpp.Count; i++)
                {
                    closestQuad = querrySplinesOpp[i];
                    for (int j = 0; j < closestQuad.Item1.Count; j++)
                    {
                        IntersectionData intersectionData = IntersectionTests.test_Intersection_Curve_Gerade(closestQuad.Item1[j], position, direction);
                        if (intersectionData.hitOpp)
                        {
                            bestData.hitOpp = true;
                            if (intersectionData.distanceOpp < bestData.distanceOpp)
                            {
                                bestData.distanceOpp = intersectionData.distanceOpp;
                                bestData.heightOpp = intersectionData.heightOpp;
                            }
                        }
                    }
                    if (bestData.hitOpp) break;
                }
            }

            return (bestData.distance, bestData.height, bestData.distanceOpp, bestData.heightOpp);
        }
    }

    class Quad
    {
        bool m_is_subdivided = false;
        Quad[] m_sub_quads = new Quad[4];

        List<BezierSpline> m_splines = new List<BezierSpline>();

        Vector2[] m_Bounds;

        QuadTree m_parentTree;

        Vector2 m_position;

        public Quad(Vector2[] Bounds, List<BezierSpline> splines, int max_spline_number, QuadTree parentTree)
        {
            m_parentTree = parentTree;
            m_Bounds = Bounds;

            for (int i = 0; i < splines.Count; i++)
            {
                if (IntersectionTests.test_Intersection_Quads(Bounds, splines[i].m_BoundingBoxes[0]))
                {
                    //Debug.Log("Added Spline");
                    m_splines.Add(splines[i]);
                    //splines[i].drawBounds();
                }
            }
            //Debug.Log(m_splines.Count);
            if (m_splines.Count > max_spline_number)
            {
                //subdivide again
                m_is_subdivided = true;
                //Debug.Log("Subdivided");
                Vector2 middelMiddel = Bounds[0] / 2 + Bounds[3] / 2;
                Vector2 middelUp = Bounds[0] / 2 + Bounds[1] / 2;
                Vector2 middelRight = Bounds[1] / 2 + Bounds[3] / 2;
                Vector2 middelDown = Bounds[2] / 2 + Bounds[3] / 2;
                Vector2 middelLeft = Bounds[0] / 2 + Bounds[2] / 2;

                Vector2[] Bounds0 = new Vector2[] { Bounds[0], middelUp, middelLeft, middelMiddel };
                m_sub_quads[0] = new Quad(Bounds0, m_splines, max_spline_number, m_parentTree);
                Vector2[] Bounds1 = new Vector2[] { middelUp, Bounds[1], middelMiddel, middelRight };
                m_sub_quads[1] = new Quad(Bounds1, m_splines, max_spline_number, m_parentTree);
                Vector2[] Bounds2 = new Vector2[] { middelLeft, middelMiddel, Bounds[2], middelDown };
                m_sub_quads[2] = new Quad(Bounds2, m_splines, max_spline_number, m_parentTree);
                Vector2[] Bounds3 = new Vector2[] { middelMiddel, middelRight, middelDown, Bounds[3] };
                m_sub_quads[3] = new Quad(Bounds3, m_splines, max_spline_number, m_parentTree);
            }

            m_position = Bounds[0] / 4 + Bounds[1] / 4 + Bounds[2] / 4 + Bounds[3] / 4;

            //Debug.DrawLine(new Vector3(Bounds[0].x, 0, Bounds[0].y), new Vector3(Bounds[1].x, 0, Bounds[1].y), Color.green, 30);
            //Debug.DrawLine(new Vector3(Bounds[2].x, 0, Bounds[2].y), new Vector3(Bounds[3].x, 0, Bounds[3].y), Color.green, 30);
            //Debug.DrawLine(new Vector3(Bounds[0].x, 0, Bounds[0].y), new Vector3(Bounds[2].x, 0, Bounds[2].y), Color.green, 30);
            //Debug.DrawLine(new Vector3(Bounds[1].x, 0, Bounds[1].y), new Vector3(Bounds[3].x, 0, Bounds[3].y), Color.green, 30);
        }

        public void traceQuad(Vector2 position, Vector2 normal, Vector2 direction)
        {
            
            if (IntersectionTests.test_Intersection_Quad_Gerade(m_Bounds, position, normal))
            {
                if (m_is_subdivided)
                {
                    //Profiler.BeginSample("Do Recursion");
                    for (int i = 0; i < 4; i++)
                    {
                        m_sub_quads[i].traceQuad(position, normal, direction);
                    }
                    //Profiler.EndSample();
                }
                else
                {
                    /*
                    Debug.DrawLine(new Vector3(m_Bounds[0].x, 0, m_Bounds[0].y), new Vector3(m_Bounds[1].x, 0, m_Bounds[1].y), Color.red, 30);
                    Debug.DrawLine(new Vector3(m_Bounds[2].x, 0, m_Bounds[2].y), new Vector3(m_Bounds[3].x, 0, m_Bounds[3].y), Color.red, 30);
                    Debug.DrawLine(new Vector3(m_Bounds[0].x, 0, m_Bounds[0].y), new Vector3(m_Bounds[2].x, 0, m_Bounds[2].y), Color.red, 30);
                    Debug.DrawLine(new Vector3(m_Bounds[1].x, 0, m_Bounds[1].y), new Vector3(m_Bounds[3].x, 0, m_Bounds[3].y), Color.red, 30);*/
                    //Profiler.BeginSample("Add Splines");
                    if (Vector2.Dot(m_position - position, direction) > 0)
                    {
                        m_parentTree.querrySplines.Add((m_splines, (position - m_position).sqrMagnitude));
                    }
                    else
                    {
                        m_parentTree.querrySplinesOpp.Add((m_splines, (position - m_position).sqrMagnitude));
                    }
                    //Profiler.EndSample();
                }
            }
        }
    }
}

