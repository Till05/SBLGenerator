using System;
using System.Collections.Generic;
using num = System.Numerics;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Profiling;

namespace LandscapeGenerator
{
    public class Chunk
    {
        public Landscape m_Landscape;

        public float[,] m_Heightmap;

        public float[,] m_HeightMapInterpolated;

        public Texture2D SandMap;

        public List<BezierSpline> m_LandscapeSplines = new List<BezierSpline>();
        public List<BezierSpline> m_SplinesInsideChunk = new List<BezierSpline>();

        public List<Vector3> m_Endpoints = new List<Vector3>();

        public int m_Res; //Number of Pixels per side
        public float m_Size;

        public int[] m_NeighborChunks = new int[8]; //Links, links oben, oben, rechts oben, rechts, rechts unten, unten, links unten
        public int index;

        public Texture2D sandTexture
        {
            get
            {
                int mapSize = SandHeight.GetLength(0);
                Texture2D sandtexture = new Texture2D(mapSize, mapSize, TextureFormat.RGB24, false);

                for (int x = 0; x < mapSize; x++)
                {
                    for (int y = 0; y < mapSize; y++)
                    {
                        if (SandHeight[x, y] > 0.1f)
                        {
                            sandtexture.SetPixel(x, y, new Color(1, 0, 0, 1));
                        }
                        else
                        {
                            sandtexture.SetPixel(x, y, new Color(0, 0, 0, 1));
                        }
                    }
                }

                return sandtexture;
            }
        }

        public Vector2 m_Pos;

        GenSettings m_Settings;

        // Internal
        num.Complex[] t = new num.Complex[3];
        float[] sampled_dists = new float[400];
        float[] sampled_heights = new float[400];
        float[] possible_Points_dists = new float[100];
        float[] possible_Points_heights = new float[100];
        float[] possible_Points_dists_minus = new float[100];
        float[] possible_Points_heights_minus = new float[100];

        public float[,] waterHeight;
        public float[,] SedimentAmount;
        Vector4[,] OutFlowFlux;
        public Vector2[,] velocity;
        float[,] hardness;
        float[,] alpha;

        float[,] d1;
        float[,] d2;
        float[,] d3;
        float[,] s1;

        float[,,] SedimentMovement;
        float[,] SandHeight;
        float[,] StoneHeight;

        public ComputeShader interpolationShader;
        public ComputeShader diffusionShader;

        public Chunk(int m_res, float size, float pos_x, float pos_y, Landscape landscape)
        {
            m_Landscape = landscape;
            m_Res = m_res;
            m_Size = size;
            m_Pos = new Vector2(pos_x, pos_y);
            m_Heightmap = new float[m_res, m_res];
            m_HeightMapInterpolated = new float[1, 1];
        }

        public void compile_relevant_Splines()
        {
            foreach (BezierSpline spline in m_LandscapeSplines)
            {
                for (int i = 0; i < spline.m_BoundingBoxes.Count; i++)
                {
                    bool inside = false;

                    foreach (Vector2 point in spline.m_BoundingBoxes[i])
                    {
                        if (!(point.x < m_Pos.x || point.y < m_Pos.y || point.x > m_Pos.x + m_Size || point.y >  m_Pos.y + m_Size))
                        {
                            inside = true;
                        }
                    }

                    if (inside)
                    {
                        List<Vector3> points = new List<Vector3>();
                        points.Add(spline.m_Points[i*3+0]);
                        points.Add(spline.m_Points[i*3+1]);
                        points.Add(spline.m_Points[i*3+2]);
                        points.Add(spline.m_Points[i*3+3]);
                        m_SplinesInsideChunk.Add(new BezierSpline(points));
                    }
                }
            }
        }

        public void GenerateChunk(GenSettings settings)
        {

            m_Settings = settings;

            float minHeight = 10000000000000000000;
            float maxHeight = -1000000000000000000;

            for (int x = 0; x < m_Res; x++)
            {
                for (int y = 0; y < m_Res; y++)
                {
                    m_Heightmap[x, y] = calPointQuadTree(new Vector2(x, y) * (m_Size / (m_Res - 1)) + m_Pos);

                    if (m_Heightmap[x, y] < minHeight)
                    {
                        minHeight = m_Heightmap[x, y];
                    }

                    if (m_Heightmap[x, y] > maxHeight)
                    {
                        maxHeight = m_Heightmap[x, y];
                    }
                }
            }
        }

        public void GenerateChunkDiffusion(GenSettings settings)
        {
            int DownscaleIts = Mathf.FloorToInt(Mathf.Log((m_Res - 2f) / 8, 2)) + 1;
//            Debug.Log(DownscaleIts);
            int relaxtIts = settings.relaxtIts;
            int discInts = settings.discInts;

            List<Texture2D> Inputs = new List<Texture2D>();
            List<Texture2D> SourceMasks = new List<Texture2D>();

            List<RenderTexture> ResultTextures = new List<RenderTexture>();
            List<RenderTexture> TempTextures = new List<RenderTexture>();

            for(int i = 0; i < DownscaleIts; i++)
            {
                int res = Mathf.FloorToInt(Mathf.Pow(2, i) * 8);
                Texture2D Input = new Texture2D(res+2, res+2, TextureFormat.R16, false);
                Texture2D SourceMask = new Texture2D(res+2, res+2, TextureFormat.R8, false);

                Inputs.Add(Input);
                SourceMasks.Add(SourceMask);

                RenderTexture ResultTexture = new RenderTexture(res + 8, res + 8, 1, RenderTextureFormat.R16);
                RenderTexture TempTexture = new RenderTexture(res, res, 1, RenderTextureFormat.R16);

                ResultTexture.enableRandomWrite = true;
                ResultTexture.Create();
                ResultTextures.Add(ResultTexture);

                TempTexture.enableRandomWrite = true;
                TempTexture.Create();
                TempTextures.Add(TempTexture);
            }

            m_Settings = settings;

            float minHeight = 10000000000000000000;
            float maxHeight = -1000000000000000000;
            
            // Spline Vorbereitung
            {
                for (int i = 0; i < DownscaleIts; i++)
                {
                    minHeight = 10000000000000000000;
                    maxHeight = -1000000000000000000;
                    int res = Mathf.FloorToInt(Mathf.Pow(2, i) * 8 + 2);
                    
                    m_Heightmap = new float[res, res];
                    
                    for (int x = 0; x < res; x++)
                    {
                        m_Heightmap[x, 0] = calPointQuadTree(new Vector2(x, 0) * (m_Size / (res - 1)) + m_Pos);
                        SourceMasks[i].SetPixel(x, 0, new Color(0, 0, 0, 0));
                    }

                    for (int x = 0; x < res; x++)
                    {
                        m_Heightmap[x, res - 1] = calPointQuadTree(new Vector2(x, res - 1) * (m_Size / (res - 1)) + m_Pos);
                        SourceMasks[i].SetPixel(x, res - 1, new Color(0, 0, 0, 0));
                    }

                    for (int y = 0; y < res; y++)
                    {
                        m_Heightmap[0, y] = calPointQuadTree(new Vector2(0, y) * (m_Size / (res - 1)) + m_Pos);
                        SourceMasks[i].SetPixel(0, y, new Color(0, 0, 0, 0));
                    }

                    for (int y = 0; y < res; y++)
                    {
                        m_Heightmap[res - 1, y] = calPointQuadTree(new Vector2(res - 1, y) * (m_Size / (res - 1)) + m_Pos);
                        SourceMasks[i].SetPixel(res - 1, y, new Color(0, 0, 0, 0));
                    }

                    for (int k = 0; k < m_LandscapeSplines.Count; k++)
                    {
                        for (int j = 0; j < (m_LandscapeSplines[k].m_Points.Count - 4) / 3 + 1; j++)
                        {
                            Vector3 point0 = m_LandscapeSplines[k].m_Points[j * 3 + 0];
                            Vector3 point1 = m_LandscapeSplines[k].m_Points[j * 3 + 1];
                            Vector3 point2 = m_LandscapeSplines[k].m_Points[j * 3 + 2];
                            Vector3 point3 = m_LandscapeSplines[k].m_Points[j * 3 + 3];

                            for (int l = 0; l < discInts; l++)
                            {
                                Vector3 point = BezierSpline.Bezier(point0, point1, point2, point3,  l / (discInts - 1f));//m_LandscapeSplines[k].m_DiscPoints[j][l];
                                int x = Mathf.FloorToInt((point.x - m_Pos.x) * ((res - 1) / m_Size));
                                int y = Mathf.FloorToInt((point.z - m_Pos.y) * ((res - 1) / m_Size)); // dit hier muss point.z sein weil y is oben

                                if (x > 1 && x < res - 1 && y > 1 && y < res - 1)
                                {
                                    m_Heightmap[x, y] = point.y;
                                    SourceMasks[i].SetPixel(x, y, new Color(0, 0, 0, 0));
                                }
                            }
                        }
                    }

                    //Debug.Log(m_SplinesInsideChunk[0].m_DiscPoints[0].Length);

                    for (int x = 0; x < res; x++)
                    {
                        for (int y = 0; y < res; y++)
                        {
                            if (m_Heightmap[x, y] != 0)
                            {
                                if (m_Heightmap[x, y] < minHeight)
                                {
                                    minHeight = m_Heightmap[x, y];
                                }

                                if (m_Heightmap[x, y] > maxHeight)
                                {
                                    maxHeight = m_Heightmap[x, y];
                                }
                            }
                        }
                    }

                    
                    for (int x = 0; x < res; x++)
                    {
                        for (int y = 0; y < res; y++)
                        {
                            float height = (m_Heightmap[x, y] - (minHeight)) / (maxHeight - minHeight);
                            Inputs[i].SetPixel(x, y, new Color(Mathf.Min(Mathf.Max(height, 0), 1), 0, 0, 1));
                        }
                    }

                    Inputs[i].Apply();
                    SourceMasks[i].Apply();
                }
            }

            //Multigrid
            
            diffusionShader.SetFloat("h", 1f);

            for (int i = 0; i < DownscaleIts - 1; i++)
            {
                // Relax
                diffusionShader.SetTexture(2, "Input", Inputs[i]);
                diffusionShader.SetTexture(0, "Input", Inputs[i]);
                diffusionShader.SetTexture(2, "SourceMask", SourceMasks[i]);
                diffusionShader.SetTexture(0, "SourceMask", SourceMasks[i]);

                diffusionShader.SetTexture(0, "Result", ResultTextures[i]);
                diffusionShader.SetTexture(0, "TempTex", TempTextures[i]);

                diffusionShader.SetTexture(1, "Result", ResultTextures[i]);
                diffusionShader.SetTexture(1, "TempTex", TempTextures[i]);

                diffusionShader.SetTexture(2, "Result", ResultTextures[i]);
                diffusionShader.SetTexture(2, "TempTex", TempTextures[i]);

                diffusionShader.Dispatch(2, ResultTextures[i].width / 8, ResultTextures[i].height / 8, 1); // initialise
                
                diffusionShader.Dispatch(1, TempTextures[i].width / 8, TempTextures[i].height / 8, 1); // Apply interpolation vom vorherigen Schritt
                
                for (int j = 0; j < relaxtIts; j++)
                {
                    diffusionShader.Dispatch(0, TempTextures[i].width / 8, TempTextures[i].height / 8, 1); // Relax
                    diffusionShader.Dispatch(1, TempTextures[i].width / 8, TempTextures[i].height / 8, 1); // Apply
                }

                // Interpolate Up
                diffusionShader.SetTexture(3, "TempTex", TempTextures[i + 1]);
                diffusionShader.SetTexture(3, "Result", ResultTextures[i]);

                diffusionShader.Dispatch(3, TempTextures[i + 1].width / 8, TempTextures[i + 1].height / 8, 1); // Interpolate*/

                //diffusionShader.SetTexture(1, "TempTex", TempTextures[i + 1]);
                //diffusionShader.SetTexture(1, "Result", ResultTextures[i + 1]);
            }
            // final one
            
            diffusionShader.SetTexture(2, "Input", Inputs[DownscaleIts - 1]);
            diffusionShader.SetTexture(0, "Input", Inputs[DownscaleIts - 1]);
            diffusionShader.SetTexture(2, "SourceMask", SourceMasks[DownscaleIts - 1]);
            diffusionShader.SetTexture(0, "SourceMask", SourceMasks[DownscaleIts - 1]);

            diffusionShader.SetTexture(0, "Result", ResultTextures[DownscaleIts - 1]);
            diffusionShader.SetTexture(0, "TempTex", TempTextures[DownscaleIts - 1]);

            diffusionShader.SetTexture(1, "Result", ResultTextures[DownscaleIts - 1]);
            diffusionShader.SetTexture(1, "TempTex", TempTextures[DownscaleIts - 1]);

            diffusionShader.SetTexture(2, "Result", ResultTextures[DownscaleIts - 1]);
            diffusionShader.SetTexture(2, "TempTex", TempTextures[DownscaleIts - 1]);

            diffusionShader.Dispatch(2, ResultTextures[DownscaleIts - 1].width / 8, ResultTextures[DownscaleIts - 1].height / 8, 1);
            diffusionShader.Dispatch(1, TempTextures[DownscaleIts - 1].width / 8, TempTextures[DownscaleIts - 1].height / 8, 1);

            for (int j = 0; j < relaxtIts; j++)
            {
                diffusionShader.Dispatch(0, TempTextures[DownscaleIts - 1].width / 8, TempTextures[DownscaleIts - 1].height / 8, 1); // Relax
                diffusionShader.Dispatch(1, TempTextures[DownscaleIts - 1].width / 8, TempTextures[DownscaleIts - 1].height / 8, 1); // Apply
            }
            

            //m_Landscape.renderTexture = ResultTextures[3];

            RenderTexture.active = ResultTextures[DownscaleIts - 1];
            Texture2D tex = new Texture2D(ResultTextures[DownscaleIts - 1].width, ResultTextures[DownscaleIts - 1].height, TextureFormat.R16, false);

            Rect rectReadPicture = new Rect(0, 0, ResultTextures[DownscaleIts - 1].width, ResultTextures[DownscaleIts - 1].height);
            tex.ReadPixels(rectReadPicture, 0, 0);

            RenderTexture.active = null;

            for (int i = 0; i < m_Res; i++)
            {
                for (int j = 0; j < m_Res; j++)
                {
                    m_Heightmap[i, j] = tex.GetPixel(i, j).r * (maxHeight - minHeight) + minHeight;
                    //m_Heightmap[i, j] = Inputs[DownscaleIts - 1].GetPixel(i, j).r * (maxHeight - minHeight) + minHeight;
                }
            }
        }

        public void GenerateInterpolationGPU()
        {
            int startres = m_Heightmap.GetLength(0);
            int finalres = (m_Heightmap.GetLength(0) - 1) * m_Settings.resAmplifier + 1;

            Texture2D input = new Texture2D(startres, startres, TextureFormat.R16, false);

            for (int x = 0; x < startres; x++)
            {
                for (int y = 0; y < startres; y++)
                {
                    input.SetPixel(x, y, new Color(m_Heightmap[x, y] / 200, 0, 0, 0));
                }
            }
            input.Apply();

            Debug.Log("interpolated");
            RenderTexture ResultTexture = new RenderTexture(finalres, finalres, 1, RenderTextureFormat.R16);
            ResultTexture.enableRandomWrite = true;
            ResultTexture.Create();

            interpolationShader.SetTexture(0, "Input", input);
            interpolationShader.SetTexture(0, "Result", ResultTexture);
            interpolationShader.SetInt("resAmplifier", m_Settings.resAmplifier);
            interpolationShader.SetInt("Resolution", finalres);
            interpolationShader.SetFloat("scale", m_Settings.scale);
            interpolationShader.SetFloat("strength", m_Settings.noise_strength);
            interpolationShader.SetFloat("lacunarity", m_Settings.lacunarity);
            interpolationShader.SetFloat("percistance", m_Settings.persistence);
            interpolationShader.SetInt("octaves", m_Settings.numOctaves);

            interpolationShader.Dispatch(0, ResultTexture.width / 8, ResultTexture.height / 8, 1);

            m_Landscape.renderTexture = ResultTexture;

            m_HeightMapInterpolated = new float[(m_Heightmap.GetLength(0) - 1) * m_Settings.resAmplifier + 1, (m_Heightmap.GetLength(1) - 1) * m_Settings.resAmplifier + 1];

            RenderTexture.active = ResultTexture;
            Texture2D tex = new Texture2D(ResultTexture.width, ResultTexture.height, TextureFormat.R16, false);

            Rect rectReadPicture = new Rect(0, 0, ResultTexture.width, ResultTexture.height);
            tex.ReadPixels(rectReadPicture, 0, 0);

            RenderTexture.active = null;

            for (int i = 0; i < ResultTexture.width - 1; i++)
            {
                for (int j = 0; j < ResultTexture.height - 1; j++)
                {
                    m_HeightMapInterpolated[i, j] = tex.GetPixel(i, j).r * 100;
                }
            }
        }

        public void GenerateInterpolation()
        {
            m_HeightMapInterpolated = new float[(m_Heightmap.GetLength(0) - 1) * m_Settings.resAmplifier + 1, (m_Heightmap.GetLength(1) - 1) * m_Settings.resAmplifier + 1];

            for (int i = 0; i < m_Heightmap.GetLength(0) - 1; i++)
            {
                for (int j = 0; j < m_Heightmap.GetLength(1) - 1; j++)
                {
                    float corner1 = m_Heightmap[i, j];
                    float corner2 = m_Heightmap[i + 1, j];
                    float corner3 = m_Heightmap[i, j + 1];
                    float corner4 = m_Heightmap[i + 1, j + 1];
                    for (int k = 0; k < m_Settings.resAmplifier + 1; k++)
                    {
                        for (int l = 0; l < m_Settings.resAmplifier + 1; l++)
                        {
                            float kf = k * 1f / m_Settings.resAmplifier;
                            float lf = l * 1f / m_Settings.resAmplifier;

                            float side1 = corner1 * (1 - kf) + corner2 * kf;
                            float side2 = corner3 * (1 - kf) + corner4 * kf;

                            float pointHeight = side1 * (1 - lf) + side2 * lf;

                            int x = i * m_Settings.resAmplifier + k;
                            int y = j * m_Settings.resAmplifier + l;

                            Vector2 Noise_Coords = m_Settings.scale * (new Vector2(x, y) * m_Size / (m_HeightMapInterpolated.GetLength(0) - 1) + m_Pos);

                            m_HeightMapInterpolated[x, y] = pointHeight + m_Settings.noise_strength;// * Noise.fbm(m_Settings.numOctaves, Noise_Coords, m_Settings.persistence, m_Settings.lacunarity);
                        }
                    }
                }
            }

            int map_res = m_HeightMapInterpolated.GetLength(0);

            waterHeight = new float[map_res, map_res];
            SedimentAmount = new float[map_res, map_res];
            OutFlowFlux = new Vector4[map_res, map_res]; //Komponenten des Vektor gehören in dieser reihenfolge zu: Left, Right, Up, Down
            velocity = new Vector2[map_res, map_res];
            hardness = new float[map_res, map_res];
            alpha = new float[map_res, map_res];

            d1 = new float[map_res, map_res];
            d2 = new float[map_res, map_res];
            d3 = new float[map_res, map_res];
            s1 = new float[map_res, map_res];

            SedimentMovement = new float[map_res, map_res, 9];
            SandHeight = new float[map_res, map_res];
            StoneHeight = new float[map_res, map_res];
            float initialSandHeight = 0.1f;

            for (int x = 0; x < map_res; x++)
            {
                for (int y = 0; y < map_res; y++)
                {
                    // Füge Regenwasser hinzu
                    //waterHeight[x, y] = 1f;
                }
            }

            for (int x = 0; x < map_res; x++)
            {
                for (int y = 0; y < map_res; y++)
                {
                    StoneHeight[x, y] = m_HeightMapInterpolated[x, y] - initialSandHeight;
                    SandHeight[x, y] = initialSandHeight;
                }
            }

        }

        public float calPoint(Vector2 point)
        {
            int sample_number = m_Settings.sampels_number;
            int number_points = 0;
            int number_points_minus = 0;

            for (int sample = 0; sample < sample_number / 2; sample++)
            {
                number_points = 0;
                number_points_minus = 0;

                Vector2 direction = new Vector2(MathF.Cos(MathF.PI * 2 * sample / sample_number), MathF.Sin(MathF.PI * 2 * sample / sample_number));

                for (int k = 0; k < m_LandscapeSplines.Count; k++)
                {
                    Vector2 PointA = point;
                    Vector2 PointB = point + direction;
                    List<Vector2[]> coefficients = m_LandscapeSplines[k].m_Coefficients;
                    List<Vector2[]> Bounds = m_LandscapeSplines[k].m_BoundingBoxes;
                    List<Vector3> points = m_LandscapeSplines[k].m_Points;

                    Vector2 AminusB = direction;
                    Vector2 normal = new Vector2(AminusB.y, -AminusB.x);

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
                                            possible_Points_dists[number_points] = (new Vector2(bezier_point.x, bezier_point.z) - PointA).magnitude;
                                            possible_Points_heights[number_points] = bezier_point.y;
                                            number_points++;
                                        }
                                        if (possible_point_sign < 0)
                                        {
                                            possible_Points_dists_minus[number_points_minus] = (new Vector2(bezier_point.x, bezier_point.z) - PointA).magnitude;
                                            possible_Points_heights_minus[number_points_minus] = bezier_point.y;
                                            number_points_minus++;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                float smalest_dist = 10000000000;
                float smalest_dist_height = 0;

                for (int i = 0; i < number_points; i++)
                {
                    if (possible_Points_dists[i] < smalest_dist)
                    {
                        smalest_dist = possible_Points_dists[i];
                        smalest_dist_height = possible_Points_heights[i];
                    }

                }

                sampled_dists[sample] = smalest_dist;
                sampled_heights[sample] = smalest_dist_height;

                smalest_dist = 10000000000;
                smalest_dist_height = 0;

                for (int i = 0; i < number_points_minus; i++)
                {
                    if (possible_Points_dists_minus[i] < smalest_dist)
                    {
                        smalest_dist = possible_Points_dists_minus[i];
                        smalest_dist_height = possible_Points_heights_minus[i];
                    }

                }

                sampled_dists[sample_number / 2 + sample] = smalest_dist;
                sampled_heights[sample_number / 2 + sample] = smalest_dist_height;
            }

            int all_points_number = m_Endpoints.Count + sample_number;

            for (int i = sample_number; i < sample_number + m_Endpoints.Count; i++)
            {
                Vector2 distVector = (new Vector2(m_Endpoints[i - sample_number].x, m_Endpoints[i - sample_number].z) - point);
                sampled_dists[i] = distVector.x * distVector.x + distVector.y * distVector.y;
                sampled_heights[i] = m_Endpoints[i - sample_number].y;
            }

            sample_number = all_points_number;


            float final_height = 0;

            for (int i = 0; i < sample_number; i++)
            {
                float alpha = 0;

                for (int j = 0; j < sample_number; j++)
                {
                    alpha += sampled_dists[i] / sampled_dists[j];
                }

                final_height += sampled_heights[i] * (1 / alpha);
            }

            return final_height;
        }

        public float calPointQuadTree(Vector2 point)
        {
            int sample_number = m_Settings.sampels_number;

            for (int sample = 0; sample < sample_number / 2; sample++)
            {
                Vector2 direction = new Vector2(MathF.Cos(MathF.PI * 2 * sample / sample_number), MathF.Sin(MathF.PI * 2 * sample / sample_number));

                Profiler.BeginSample("traceTree");
                (float, float, float, float) intersectionData = m_Landscape.quadTree.traceTree(point, direction);
                Profiler.EndSample();

                sampled_dists[sample * 2] = intersectionData.Item1;
                sampled_dists[sample * 2 + 1] = intersectionData.Item3;

                sampled_heights[sample * 2] = intersectionData.Item2;
                sampled_heights[sample * 2 + 1] = intersectionData.Item4;
            }

            float final_height = 0;

            for (int i = 0; i < sample_number; i++)
            {
                float alpha = 0;

                for (int j = 0; j < sample_number; j++)
                {
                    alpha += sampled_dists[i] / sampled_dists[j];
                }

                final_height += sampled_heights[i] * (1 / alpha);
            }

            return final_height;
        }

        public float[,] erodeChunk(int timesteps)
        {
            //Settings
            int map_res = m_HeightMapInterpolated.GetLength(0);
            float deltat = 1f;
            //int timesteps = 600;

            float rainrate = 0.1f;
            float SedimentCapacity = 0.1f;
            float ErosionSpeed = 0.5f;
            float DeposionSpeed = 0.5f;
            float evaporationSpeeed = 0.1f;
            float flowSpeed = 0.2f; // im Paper ist von virtuellen Rohren die rede, die jede Zelle miteinander verbinden. Dieser Parameter ist A*l*g zusammengefasst
            float girdCellLength = m_Size / (map_res - 1);

            //initialise Main Fields

            float VolumeChange = 0;
            float TerrainVolumeChange = 0;


            for (int x = 0; x < map_res; x++)
            {
                for (int y = 0; y < map_res; y++)
                {
                    hardness[x, y] = 0.5f;
                }
            }

            //initialire nebenfelder


            /*
            for (int x = 0; x < map_res; x++)
            {
                for (int y = 0; y < map_res; y++)
                {
                    // Füge Regenwasser hinzu
                    waterHeight[x, y] = 1;
                }
            }*/

            for (int t = 0; t < timesteps; t++)
            {

                //Führe eine inetration durch
                for (int x = 0; x < map_res; x++)
                {
                    for (int y = 0; y < map_res; y++)
                    {
                        // Füge Regenwasser hinzu
                        d1[x, y] = waterHeight[x, y] + rainrate * deltat;
                    }
                }

                for (int x = 0; x < map_res; x++)
                {
                    for (int y = 0; y < map_res; y++)
                    {
                        // Berechne Wasserfluss
                        // Wasserfluss ist immer größer als null, da jede zelle nur betrachtet, was raus fließt, nicht was rein fließt
                        float heightDifferenceLeft = m_HeightMapInterpolated[x, y] + d1[x, y] - m_HeightMapInterpolated[Noise.mod(x - 1, map_res), y] - d1[Noise.mod(x - 1, map_res), y];
                        float flowLeft = Mathf.Max(0, OutFlowFlux[x, y].x + deltat * flowSpeed * heightDifferenceLeft);

                        float heightDifferenceRight = m_HeightMapInterpolated[x, y] + d1[x, y] - m_HeightMapInterpolated[Noise.mod(x + 1, map_res), y] - d1[Noise.mod(x + 1, map_res), y];
                        float flowRight = Mathf.Max(0, OutFlowFlux[x, y].y + deltat * flowSpeed * heightDifferenceRight);

                        float heightDifferenceUp = m_HeightMapInterpolated[x, y] + d1[x, y] - m_HeightMapInterpolated[x, Noise.mod(y - 1, map_res)] - d1[x, Noise.mod(y - 1, map_res)];
                        float flowUp = Mathf.Max(0, OutFlowFlux[x, y].z + deltat * flowSpeed * heightDifferenceUp);

                        float heightDifferenceDown = m_HeightMapInterpolated[x, y] + d1[x, y] - m_HeightMapInterpolated[x, Noise.mod(y + 1, map_res)] - d1[x, Noise.mod(y + 1, map_res)];
                        float flowDown = Mathf.Max(0, OutFlowFlux[x, y].w + deltat * flowSpeed * heightDifferenceDown);

                        // Der gesammte Wasserfluss darf nicht größer als die vorhandene Menge Wasser sein, daher wird sie skalliert
                        float K = 1;
                        if ((flowLeft + flowRight + flowUp + flowDown) != 0)
                        {
                            K = Mathf.Min(1, (d1[x, y] /* girdCellLength * girdCellLength*/) / ((flowLeft + flowRight + flowUp + flowDown) * deltat));
                        }

                        Vector2 gradient = new Vector2(-heightDifferenceLeft + heightDifferenceRight, -heightDifferenceUp + heightDifferenceDown) / (girdCellLength * 2);
                        Vector3 normal = new Vector3(gradient.x, gradient.y, -1).normalized;
                        alpha[x, y] = Mathf.Acos(Mathf.Abs(Vector3.Dot(normal, new Vector3(0, 0, 1))));

                        OutFlowFlux[x, y] = new Vector4(flowLeft, flowRight, flowUp, flowDown) * K;
                    }
                }

                for (int x = 0; x < map_res; x++)
                {
                    for (int y = 0; y < map_res; y++)
                    {
                        // die Volumenänderung aus allen ein und ausflüssen berechnen
                        float deltaV = deltat * (OutFlowFlux[Noise.mod(x - 1, map_res), y].y + OutFlowFlux[x, Noise.mod(y + 1, map_res)].z + OutFlowFlux[Noise.mod(x + 1, map_res), y].x + OutFlowFlux[x, Noise.mod(y - 1, map_res)].w -
                                                 (OutFlowFlux[x, y].x + OutFlowFlux[x, y].y + OutFlowFlux[x, y].z + OutFlowFlux[x, y].w)); // hier habe ich oben und unten im vergleich zum Paper verändert



                        d2[x, y] = d1[x, y] + deltaV;// / (girdCellLength * girdCellLength);

                        /*
                        if (d2[x, y] < 0)
                        {
                            Debug.Log(new Vector2(x, y));
                            Debug.Log("FlowIn: " + (OutFlowFlux[Noise.mod(x - 1, map_res), y].y + OutFlowFlux[x, Noise.mod(y + 1, map_res)].z + OutFlowFlux[Noise.mod(x + 1, map_res), y].x + OutFlowFlux[x, Noise.mod(y - 1, map_res)].w));
                            Debug.Log("FlowOut: " + (OutFlowFlux[x, y].x + OutFlowFlux[x, y].y + OutFlowFlux[x, y].z + OutFlowFlux[x, y].w));
                            Debug.Log("WaterHeight:" + d2[x, y]);
                        }*/


                        float vX;
                        float vY;

                        if ((d1[x, y] + d2[x, y]) == 0)
                        {
                            vX = 0;
                            vY = 0;
                        }
                        else
                        {
                            vX = (OutFlowFlux[Noise.mod(x - 1, map_res), y].y - OutFlowFlux[x, y].x - OutFlowFlux[Noise.mod(x + 1, map_res), y].x + OutFlowFlux[x, y].y) / 2; //((d1[x, y] + d2[x, y]) * girdCellLength);
                            vY = (OutFlowFlux[x, Noise.mod(y - 1, map_res)].w - OutFlowFlux[x, y].z - OutFlowFlux[x, Noise.mod(y + 1, map_res)].z + OutFlowFlux[x, y].w) / 2; // ((d1[x, y] + d2[x, y]) * girdCellLength);
                        }

                        velocity[x, y] = new Vector2(vX, vY);


                        float C = SedimentCapacity * velocity[x, y].magnitude * 0.5f;// * Mathf.Sin(alpha[x,y]);// * 1/d1[x,y]; // hier fehlt ein Sin() mit einen komischen alpha
                        //Debug.Log("C: " + C);
                        //Debug.Log("SedimentAmount: " + SedimentAmount[x, y]);

                        if (SedimentAmount[x, y] < C)
                        {
                            //float erosionAmount = Mathf.Min(d2[x, y], deltat * hardness[x, y] * ErosionSpeed * (C - SedimentAmount[x, y]));
                            float erosionAmount = deltat * hardness[x, y] * ErosionSpeed * (C - SedimentAmount[x, y]);
                            m_HeightMapInterpolated[x, y] = m_HeightMapInterpolated[x, y] - erosionAmount;
                            TerrainVolumeChange -= erosionAmount;
                            s1[x, y] = SedimentAmount[x, y] + erosionAmount;
                            d3[x, y] = d2[x, y] + erosionAmount;
                            //Debug.Log("erosion Amount: " + erosionAmount);
                        }
                        else
                        {
                            float depositionAmount = Mathf.Min(SedimentAmount[x, y], d2[x, y], deltat * DeposionSpeed * (SedimentAmount[x, y] - C));
                            //float depositionAmount = deltat * DeposionSpeed * (SedimentAmount[x, y] - C);
                            m_HeightMapInterpolated[x, y] = m_HeightMapInterpolated[x, y] + depositionAmount;
                            TerrainVolumeChange += depositionAmount;
                            s1[x, y] = SedimentAmount[x, y] - depositionAmount;
                            d3[x, y] = d2[x, y] - depositionAmount;
                            //Debug.Log("depostion Amount: " + depositionAmount);
                        }
                        //d3[x, y] = d2[x, y];

                        //Debug.Log(m_HeightMapInterpolated[x, y]);


                    }
                }

                for (int x = 0; x < map_res; x++)
                {
                    for (int y = 0; y < map_res; y++)
                    {

                        Vector2 depostionPoint = new Vector2(x + velocity[x, y].x * deltat, y - velocity[x, y].y * deltat);
                        // Bi-lineare Interpolation

                        int depX = Mathf.FloorToInt(depostionPoint.x);
                        int depY = Mathf.FloorToInt(depostionPoint.y);

                        float u = depostionPoint.x - depX;
                        float v = depostionPoint.y - depY;

                        //Debug.Log(new Vector2(x, y));

                        //Debug.Log(u);
                        //Debug.Log(v);
                        //Debug.Log(depX);
                        //Debug.Log(depY);
                        //Debug.Log(velocity[x,y]);

                        depX = Noise.mod(depX, map_res);
                        depY = Noise.mod(depY, map_res);

                        float corner1 = s1[depX, depY];
                        float corner2 = s1[Noise.mod(depX - 1, map_res), depY];
                        float corner3 = s1[depX, Noise.mod(depY + 1, map_res)];
                        float corner4 = s1[Noise.mod(depX - 1, map_res), Noise.mod(depY + 1, map_res)];

                        //SedimentAmount[x, y] = (corner1 * (1 - v) + corner2 * v) * (1 - u) + (corner3 * (1 - v) + corner4 * v) * u;
                        SedimentAmount[x, y] = (corner1 * (1 - u) + corner2 * u) * (1 - v) + (corner3 * (1 - u) + corner4 * u) * v;

                        //waterHeight[x, y] = Mathf.Max(0, d2[x, y] - evaporationSpeeed * deltat);

                        VolumeChange += d3[x, y] * (1 - evaporationSpeeed * deltat) - waterHeight[x, y];
                        //VolumeChange += d3[x, y] - waterHeight[x, y] - rainrate * deltat;

                        //d2 muss d3 werden damit die Erosion funktioniert
                        waterHeight[x, y] = d3[x, y] * (1 - evaporationSpeeed * deltat);
                        //waterHeight[x, y] = Mathf.Max(d3[x, y] - evaporationSpeeed * deltat, 0);
                    }
                }
                //Debug.Log("Water Volume Change:" + VolumeChange);
                //Debug.Log("TerrainVolumeChange" + TerrainVolumeChange);
            }

            //Debug.Log(VolumeChange);

            return waterHeight;
        }

        public void CalculateSedimentMovement()
        {
            //Settings
            int map_res = m_HeightMapInterpolated.GetLength(0);
            float deltat = 1f;

            float ThermalErosionSpeed = 0.5f;
            float max_angle = 0.4f;
            float girdCellLength = m_Size / (map_res - 1);
            float girdCellLengthDiagonal = Mathf.Sqrt(2 * girdCellLength * girdCellLength);
            float hardness = 0.5f;

            for (int x = 0; x < map_res - 1; x++)
            {
                for (int y = 0; y < map_res - 1; y++)
                {
                    float ownHeight = SandHeight[x, y] + StoneHeight[x, y];
                    float[] Differences = new float[8];


                    if (x == 0)
                    {
                        if (y == 0)
                        {
                            Differences[0] = ownHeight - SandHeight[x + 1, y] - StoneHeight[x + 1, y];
                            Differences[1] = ownHeight - SandHeight[x + 1, y + 1] - StoneHeight[x + 1, y + 1];
                            Differences[2] = ownHeight - SandHeight[x, y + 1] - StoneHeight[x, y + 1];

                            Chunk neiChuL = m_Landscape.m_Chunks[m_NeighborChunks[0]];
                            Chunk neiChuOL = m_Landscape.m_Chunks[m_NeighborChunks[1]];
                            Chunk neiChuO = m_Landscape.m_Chunks[m_NeighborChunks[2]];

                            Differences[3] = ownHeight - neiChuL.SandHeight[map_res - 2, y + 1] - neiChuL.StoneHeight[map_res - 2, y + 1];
                            Differences[4] = ownHeight - neiChuL.SandHeight[map_res - 2, y] - neiChuL.StoneHeight[map_res - 2, y];
                            Differences[5] = ownHeight - neiChuOL.SandHeight[map_res - 2, map_res - 2] - neiChuOL.StoneHeight[map_res - 2, map_res - 2];
                            Differences[6] = ownHeight - SandHeight[x, map_res - 2] - StoneHeight[x, map_res - 2];
                            Differences[7] = ownHeight - neiChuO.SandHeight[x + 1, map_res - 2] - neiChuO.StoneHeight[x + 1, map_res - 2];
                        }
                        else
                        {
                            Differences[0] = ownHeight - SandHeight[x + 1, y] - StoneHeight[x + 1, y];
                            Differences[1] = ownHeight - SandHeight[x + 1, y + 1] - StoneHeight[x + 1, y + 1];
                            Differences[2] = ownHeight - SandHeight[x, y + 1] - StoneHeight[x, y + 1];

                            Chunk neiChuL = m_Landscape.m_Chunks[m_NeighborChunks[0]];

                            Differences[3] = ownHeight - neiChuL.SandHeight[map_res - 2, y + 1] - neiChuL.StoneHeight[map_res - 2, y + 1];
                            Differences[4] = ownHeight - neiChuL.SandHeight[map_res - 2, y] - neiChuL.StoneHeight[map_res - 2, y];
                            Differences[5] = ownHeight - neiChuL.SandHeight[map_res - 2, y - 1] - neiChuL.StoneHeight[map_res - 2, y - 1];

                            Differences[6] = ownHeight - SandHeight[x, y - 1] - StoneHeight[x, y - 1];
                            Differences[7] = ownHeight - SandHeight[x + 1, y - 1] - StoneHeight[x + 1, y - 1];
                        }
                    }
                    else if (y == 0)
                    {
                        Differences[0] = ownHeight - SandHeight[x + 1, y] - StoneHeight[x + 1, y];
                        Differences[1] = ownHeight - SandHeight[x + 1, y + 1] - StoneHeight[x + 1, y + 1];
                        Differences[2] = ownHeight - SandHeight[x, y + 1] - StoneHeight[x, y + 1];
                        Differences[3] = ownHeight - SandHeight[x - 1, y + 1] - StoneHeight[x - 1, y + 1];
                        Differences[4] = ownHeight - SandHeight[x - 1, y] - StoneHeight[x - 1, y];

                        Chunk neiChuO = m_Landscape.m_Chunks[m_NeighborChunks[2]];

                        Differences[5] = ownHeight - neiChuO.SandHeight[x - 1, map_res - 2] - neiChuO.StoneHeight[x - 1, map_res - 2];
                        Differences[6] = ownHeight - neiChuO.SandHeight[x, map_res - 2] - neiChuO.StoneHeight[x, map_res - 2];
                        Differences[7] = ownHeight - neiChuO.SandHeight[x + 1, map_res - 2] - neiChuO.StoneHeight[x + 1, map_res - 2];
                    }
                    else
                    {
                        Differences[0] = ownHeight - SandHeight[x + 1, y] - StoneHeight[x + 1, y];
                        Differences[1] = ownHeight - SandHeight[x + 1, y + 1] - StoneHeight[x + 1, y + 1];
                        Differences[2] = ownHeight - SandHeight[x, y + 1] - StoneHeight[x, y + 1];
                        Differences[3] = ownHeight - SandHeight[x - 1, y + 1] - StoneHeight[x - 1, y + 1];
                        Differences[4] = ownHeight - SandHeight[x - 1, y] - StoneHeight[x - 1, y];
                        Differences[5] = ownHeight - SandHeight[x - 1, y - 1] - StoneHeight[x - 1, y - 1];
                        Differences[6] = ownHeight - SandHeight[x, y - 1] - StoneHeight[x, y - 1];
                        Differences[7] = ownHeight - SandHeight[x + 1, y - 1] - StoneHeight[x + 1, y - 1];
                    }

                    float H = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        if (H < Differences[i])
                        {
                            H = Differences[i];
                        }
                    }
                    //Debug.Log(SedimentMovement[x,y,0]);
                    SedimentMovement[x, y, 0] = -Mathf.Min(deltat * ThermalErosionSpeed * hardness * H / 2, SandHeight[x, y]);

                    if (SedimentMovement[x, y, 0] != 0)
                    {
                        float HeightDifferenceSumm = 0;

                        for (int i = 0; i < 4; i++)
                        {
                            float angle = (Differences[2 * i]) / girdCellLength;
                            if (angle > max_angle)
                            {
                                HeightDifferenceSumm += Differences[2 * i];
                            }
                        }

                        for (int i = 0; i < 4; i++)
                        {
                            float angle = (Differences[2 * i + 1]) / girdCellLengthDiagonal;
                            if (angle > max_angle)
                            {
                                HeightDifferenceSumm += Differences[2 * i + 1];
                            }
                        }

                        if (HeightDifferenceSumm == 0)
                        {
                            SedimentMovement[x, y, 0] = 0;
                        }
                        for (int i = 0; i < 4; i++)
                        {
                            float angle = (Differences[2 * i]) / girdCellLength;
                            if (angle > max_angle)
                            {
                                SedimentMovement[x, y, 1 + 2 * i] = -SedimentMovement[x, y, 0] * (Differences[2 * i]) / HeightDifferenceSumm;
                            }
                            else
                            {
                                SedimentMovement[x, y, 1 + 2 * i] = 0;
                            }
                        }

                        for (int i = 0; i < 4; i++)
                        {
                            float angle = (Differences[2 * i + 1]) / girdCellLengthDiagonal;
                            if (angle > max_angle)
                            {
                                SedimentMovement[x, y, 1 + 2 * i + 1] = -SedimentMovement[x, y, 0] * (Differences[2 * i + 1]) / HeightDifferenceSumm;
                            }
                            else
                            {
                                SedimentMovement[x, y, 1 + 2 * i + 1] = 0;
                            }
                        }
                    }
                }

            }
        }

        public void ApplySedimentMovement()
        {
            int map_res = m_HeightMapInterpolated.GetLength(0);
            float Volume = 0;
            float VolumeChange = 0;
            for (int x = 0; x < map_res - 1; x++)
            {
                for (int y = 0; y < map_res - 1; y++)
                {
                    if (x == 0)
                    {
                        if (y == 0)
                        {
                            Chunk neiChuL = m_Landscape.m_Chunks[m_NeighborChunks[0]];
                            Chunk neiChuOL = m_Landscape.m_Chunks[m_NeighborChunks[1]];
                            Chunk neiChuO = m_Landscape.m_Chunks[m_NeighborChunks[2]];

                            SandHeight[x, y] += SedimentMovement[x, y, 0];
                            SandHeight[x, y] += neiChuL.SedimentMovement[map_res - 2, y, 1];
                            SandHeight[x, y] += neiChuOL.SedimentMovement[map_res - 2, map_res - 2, 2];
                            SandHeight[x, y] += neiChuO.SedimentMovement[x, map_res - 2, 3];
                            SandHeight[x, y] += neiChuO.SedimentMovement[x + 1, map_res - 2, 4];
                            SandHeight[x, y] += SedimentMovement[x + 1, y, 5];
                            SandHeight[x, y] += SedimentMovement[x + 1, y + 1, 6];
                            SandHeight[x, y] += SedimentMovement[x, y + 1, 7];
                            SandHeight[x, y] += neiChuL.SedimentMovement[map_res - 2, y + 1, 8];
                        }
                        else
                        {
                            Chunk neiChuL = m_Landscape.m_Chunks[m_NeighborChunks[0]];

                            SandHeight[x, y] += SedimentMovement[x, y, 0];
                            SandHeight[x, y] += neiChuL.SedimentMovement[map_res - 2, y, 1];
                            SandHeight[x, y] += neiChuL.SedimentMovement[map_res - 2, y - 1, 2];
                            SandHeight[x, y] += SedimentMovement[x, y - 1, 3];
                            SandHeight[x, y] += SedimentMovement[x + 1, y - 1, 4];
                            SandHeight[x, y] += SedimentMovement[x + 1, y, 5];
                            SandHeight[x, y] += SedimentMovement[x + 1, y + 1, 6];
                            SandHeight[x, y] += SedimentMovement[x, y + 1, 7];
                            SandHeight[x, y] += neiChuL.SedimentMovement[map_res - 2, y + 1, 8];
                        }
                    }
                    else if (y == 0)
                    {
                        Chunk neiChuO = m_Landscape.m_Chunks[m_NeighborChunks[2]];

                        SandHeight[x, y] += SedimentMovement[x, y, 0];
                        SandHeight[x, y] += SedimentMovement[x - 1, y, 1];
                        SandHeight[x, y] += neiChuO.SedimentMovement[x - 1, map_res - 2, 2];
                        SandHeight[x, y] += neiChuO.SedimentMovement[x, map_res - 2, 3];
                        SandHeight[x, y] += neiChuO.SedimentMovement[x + 1, map_res - 2, 4];
                        SandHeight[x, y] += SedimentMovement[x + 1, y, 5];
                        SandHeight[x, y] += SedimentMovement[x + 1, y + 1, 6];
                        SandHeight[x, y] += SedimentMovement[x, y + 1, 7];
                        SandHeight[x, y] += SedimentMovement[x - 1, y + 1, 8];
                    }
                    else
                    {
                        SandHeight[x, y] += SedimentMovement[x, y, 0];
                        SandHeight[x, y] += SedimentMovement[x - 1, y, 1];
                        SandHeight[x, y] += SedimentMovement[x - 1, y - 1, 2];
                        SandHeight[x, y] += SedimentMovement[x, y - 1, 3];
                        SandHeight[x, y] += SedimentMovement[x + 1, y - 1, 4];
                        SandHeight[x, y] += SedimentMovement[x + 1, y, 5];
                        SandHeight[x, y] += SedimentMovement[x + 1, y + 1, 6];
                        SandHeight[x, y] += SedimentMovement[x, y + 1, 7];
                        SandHeight[x, y] += SedimentMovement[x - 1, y + 1, 8];
                    }
                    /*
                    SandHeight[x, y] += SedimentMovement[x, y, 0];
                    SandHeight[x, y] += SedimentMovement[Noise.mod(x - 1, map_res), y, 1];
                    SandHeight[x, y] += SedimentMovement[Noise.mod(x - 1, map_res), Noise.mod(y - 1, map_res), 2];
                    SandHeight[x, y] += SedimentMovement[x, Noise.mod(y - 1, map_res), 3];
                    SandHeight[x, y] += SedimentMovement[Noise.mod(x + 1, map_res), Noise.mod(y - 1, map_res), 4];
                    SandHeight[x, y] += SedimentMovement[Noise.mod(x + 1, map_res), y, 5];
                    SandHeight[x, y] += SedimentMovement[Noise.mod(x + 1, map_res), Noise.mod(y + 1, map_res), 6];
                    SandHeight[x, y] += SedimentMovement[x, Noise.mod(y + 1, map_res), 7];
                    SandHeight[x, y] += SedimentMovement[Noise.mod(x - 1, map_res), Noise.mod(y + 1, map_res), 8];*/

                    for (int i = 0; i < 9; i++)
                    {
                        VolumeChange += SedimentMovement[x, y, i];
                    }

                    //Volume += m_HeightMapInterpolated[x, y];
                }
            }
        }

        public void ApplyErosion()
        {
            int map_res = m_HeightMapInterpolated.GetLength(0);

            for (int x = 0; x < map_res - 1; x++)
            {
                for (int y = 0; y < map_res - 1; y++)
                {
                    m_HeightMapInterpolated[x, y] = SandHeight[x, y] + StoneHeight[x, y];
                }
            }

            Chunk neiChuL = m_Landscape.m_Chunks[m_NeighborChunks[0]];
            Chunk neiChuOL = m_Landscape.m_Chunks[m_NeighborChunks[1]];
            Chunk neiChuO = m_Landscape.m_Chunks[m_NeighborChunks[2]];

            neiChuL.m_HeightMapInterpolated[map_res - 1, 0] = m_HeightMapInterpolated[0, 0];
            neiChuOL.m_HeightMapInterpolated[map_res - 1, map_res - 1] = m_HeightMapInterpolated[0, 0];
            neiChuO.m_HeightMapInterpolated[0, map_res - 1] = m_HeightMapInterpolated[0, 0];

            for (int i = 1; i < map_res - 1; i++)
            {
                neiChuL.m_HeightMapInterpolated[map_res - 1, i] = m_HeightMapInterpolated[0, i];
            }

            for (int i = 1; i < map_res - 1; i++)
            {
                neiChuO.m_HeightMapInterpolated[i, map_res - 1] = m_HeightMapInterpolated[i, 0];
            }
        }

    }

}
