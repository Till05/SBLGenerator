using System;
using System.Collections.Generic;
using num = System.Numerics;
using UnityEngine;
using UnityEngine.Jobs;

namespace LandscapeGenerator
{
    public class Chunk
    {
        public Landscape m_Landscape;

        public float[,] m_Heightmap;

        public float[,] m_HeightMapInterpolated;
        public float[,] m_WaterHeightMap;

        public List<BezierSpline> m_LandscapeSplines = new List<BezierSpline>();

        public List<Vector3> m_Endpoints = new List<Vector3>();

        public int m_Res; //Number of Pixels per side
        public float m_Size;

        public int[] m_NeighborChunks = new int[8]; //Links, links oben, oben, rechts oben, rechts, rechts unten, unten, links unten
        public int index;

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

        public ComputeShader interpolationShader;
        public ComputeShader erosionShader;
        public ComputeShader diffusionShader;
        public RenderTexture renderTexture;
        public RenderTexture TempTexture;

        // Erosion Stuff
        private readonly string[] _kernelNames = {
        "RainAndControl",
        "FluxComputation",
        "FluxApply",
        "HydraulicErosion",
        "SedimentAdvection",
        "ThermalErosion",
        "ApplyThermalErosion"
        };

        // Kernel-related data
        private int[] _kernels;
        private uint _threadsPerGroupX;
        private uint _threadsPerGroupY;
        private uint _threadsPerGroupZ;
        public Texture2D InitialState;
        public Material InitHeightMap;

        [Header("Simulation settings")]
        [Range(32, 1024)]
        public int Width = 256;
        [Range(32, 1024)]
        public int Height = 256;
        public SimulationSettings Settings;

        // State texture ARGBFloat
        // R - surface height  [0, +inf]
        // G - water over surface height [0, +inf]
        // B - Suspended sediment amount [0, +inf]
        // A - Hardness of the surface [0, 1]
        private RenderTexture _stateTexture;
        private RenderTexture _stateTextureBuffer;

        // Output water flux-field texture
        // represents how much water is OUTGOING in each direction
        // R - flux to the left cell [0, +inf]
        // G - flux to the right cell [0, +inf]
        // B - flux to the top cell [0, +inf]
        // A - flux to the bottom cell [0, +inf]
        private RenderTexture _waterFluxTexture;

        // Output terrain flux-field texture
        // represents how much landmass is OUTGOING in each direction
        // Used in thermal erosion process
        // R - flux to the left cell [0, +inf]
        // G - flux to the right cell [0, +inf]
        // B - flux to the top cell [0, +inf]
        // A - flux to the bottom cell [0, +inf]
        private RenderTexture _terrainFluxTexture;

        // Velocity texture
        // R - X-velocity [-inf, +inf]
        // G - Y-velocity [-inf, +inf]
        private RenderTexture _velocityTexture;

        public Chunk(int res, float size, float pos_x, float pos_y, Landscape landscape)
        {
            m_Landscape = landscape;
            m_Res = res;
            m_Size = size;
            m_Pos = new Vector2(pos_x, pos_y);
            m_Heightmap = new float[res, res];
            m_HeightMapInterpolated = new float[1, 1];
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
                    m_Heightmap[x, y] = calPoint(new Vector2(x, y) * (m_Size / (m_Res - 1)) + m_Pos);

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
            int discInts = 200;

            m_Settings = settings;
            Texture2D Input = new Texture2D(m_Res, m_Res, TextureFormat.R16, false);
            Texture2D SourceMask = new Texture2D(m_Res, m_Res, TextureFormat.R8, false);

            for (int x = 0; x < m_Res; x++)
            {
                m_Heightmap[x, 0] = calPoint(new Vector2(x, 0) * (m_Size / (m_Res - 1)) + m_Pos);
                SourceMask.SetPixel(x, 0, new Color(0, 0, 0, 0));
            }

            for (int x = 0; x < m_Res; x++)
            {
                m_Heightmap[x, m_Res - 1] = calPoint(new Vector2(x, m_Res - 1) * (m_Size / (m_Res - 1)) + m_Pos);
                SourceMask.SetPixel(x, m_Res - 1, new Color(0, 0, 0, 0));
            }

            for (int y = 0; y < m_Res; y++)
            {
                m_Heightmap[0, y] = calPoint(new Vector2(0, y) * (m_Size / (m_Res - 1)) + m_Pos);
                SourceMask.SetPixel(0, y, new Color(0, 0, 0, 0));
            }

            for (int y = 0; y < m_Res; y++)
            {
                m_Heightmap[m_Res - 1, y] = calPoint(new Vector2(m_Res - 1, y) * (m_Size / (m_Res - 1)) + m_Pos);
                SourceMask.SetPixel(m_Res - 1, y, new Color(0, 0, 0, 0));
            }

            for (int i = 0; i < m_LandscapeSplines.Count; i++)
            {
                for (int j = 0; j < (m_LandscapeSplines[i].m_Points.Count - 4) / 3 + 1; j++)
                {
                    Vector3 point0 = m_LandscapeSplines[i].m_Points[j * 3 + 0];
                    Vector3 point1 = m_LandscapeSplines[i].m_Points[j * 3 + 1];
                    Vector3 point2 = m_LandscapeSplines[i].m_Points[j * 3 + 2];
                    Vector3 point3 = m_LandscapeSplines[i].m_Points[j * 3 + 3];

                    for (int k = 0; k < discInts; k++)
                    {
                        Vector3 point = BezierSpline.Bezier(point0, point1, point2, point3, k / (discInts - 1f));
                        int x = Mathf.FloorToInt((point.x - m_Pos.x) * ((m_Res - 1) / m_Size));
                        int y = Mathf.FloorToInt((point.z - m_Pos.y) * ((m_Res - 1) / m_Size)); // dit hier muss point.z sein weil y is oben

                        if (x > 1 && x < m_Res - 1 && y > 1 && y < m_Res - 1)
                        {
                            m_Heightmap[x, y] = point.y;
                            SourceMask.SetPixel(x, y, new Color(0, 0, 0, 0));
                        }
                    }
                }
            }

            float minHeight = 10000000000000000000;
            float maxHeight = -1000000000000000000;

            for (int x = 0; x < m_Res; x++)
            {
                for (int y = 0; y < m_Res; y++)
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

            for (int x = 0; x < m_Res; x++)
            {
                for (int y = 0; y < m_Res; y++)
                {
                    Input.SetPixel(x, y, new Color((m_Heightmap[x, y] - minHeight) / (maxHeight - minHeight), 0, 0, 1));
                }
            }

            Input.Apply();
            SourceMask.Apply();

            renderTexture = new RenderTexture(m_Res + 6, m_Res + 6, 1, RenderTextureFormat.R16);
            renderTexture.enableRandomWrite = true;
            renderTexture.Create();

            TempTexture = new RenderTexture(m_Res - 2, m_Res - 2, 1, RenderTextureFormat.R16);
            TempTexture.enableRandomWrite = true;
            TempTexture.Create();

            diffusionShader.SetFloat("h", 1f);

            diffusionShader.SetTexture(2, "Input", Input);
            diffusionShader.SetTexture(0, "Input", Input);
            diffusionShader.SetTexture(2, "SourceMask", SourceMask);
            diffusionShader.SetTexture(0, "SourceMask", SourceMask);

            diffusionShader.SetTexture(0, "Result", renderTexture);
            diffusionShader.SetTexture(0, "TempTex", TempTexture);

            diffusionShader.SetTexture(1, "Result", renderTexture);
            diffusionShader.SetTexture(1, "TempTex", TempTexture);

            diffusionShader.SetTexture(2, "Result", renderTexture);
            diffusionShader.SetTexture(2, "TempTex", TempTexture);

            diffusionShader.Dispatch(2, renderTexture.width / 8, renderTexture.height / 8, 1);

            for (int i = 0; i < 10000; i++)
            {
                diffusionShader.Dispatch(0, TempTexture.width / 8, TempTexture.height / 8, 1);
                diffusionShader.Dispatch(1, TempTexture.width / 8, TempTexture.height / 8, 1);
            }

            m_Landscape.renderTexture = renderTexture;

            RenderTexture.active = renderTexture;
            Texture2D tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.R16, false);

            Rect rectReadPicture = new Rect(0, 0, renderTexture.width, renderTexture.height);
            tex.ReadPixels(rectReadPicture, 0, 0);

            RenderTexture.active = null;

            for (int i = 0; i < m_Res; i++)
            {
                for (int j = 0; j < m_Res; j++)
                {
                    m_Heightmap[i, j] = tex.GetPixel(i, j).r * (maxHeight - minHeight) + minHeight;
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
            renderTexture = new RenderTexture(finalres, finalres, 1, RenderTextureFormat.R16);
            renderTexture.enableRandomWrite = true;
            renderTexture.Create();

            interpolationShader.SetTexture(0, "Input", input);
            interpolationShader.SetTexture(0, "Result", renderTexture);
            interpolationShader.SetInt("resAmplifier", m_Settings.resAmplifier);
            interpolationShader.SetInt("Resolution", finalres);
            interpolationShader.SetFloat("scale", m_Settings.scale);
            interpolationShader.SetFloat("strength", m_Settings.noise_strength);
            interpolationShader.SetFloat("lacunarity", m_Settings.lacunarity);
            interpolationShader.SetFloat("percistance", m_Settings.persistence);
            interpolationShader.SetInt("octaves", m_Settings.numOctaves);

            interpolationShader.Dispatch(0, renderTexture.width / 8, renderTexture.height / 8, 1);

            m_Landscape.renderTexture = renderTexture;

            m_HeightMapInterpolated = new float[(m_Heightmap.GetLength(0) - 1) * m_Settings.resAmplifier + 1, (m_Heightmap.GetLength(1) - 1) * m_Settings.resAmplifier + 1];

            RenderTexture.active = renderTexture;
            Texture2D tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.R16, false);

            Rect rectReadPicture = new Rect(0, 0, renderTexture.width, renderTexture.height);
            tex.ReadPixels(rectReadPicture, 0, 0);

            RenderTexture.active = null;

            for (int i = 0; i < renderTexture.width - 1; i++)
            {
                for (int j = 0; j < renderTexture.height - 1; j++)
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

                            m_HeightMapInterpolated[x, y] = pointHeight + m_Settings.noise_strength * Noise.fbm(m_Settings.numOctaves, Noise_Coords, m_Settings.persistence, m_Settings.lacunarity);
                        }
                    }
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

        public void InitialiseErosion()
        {
            int res = m_HeightMapInterpolated.GetLength(0);
            Debug.Log(res);
            m_WaterHeightMap = new float[res, res];

            InitialState = new Texture2D(res, res, TextureFormat.RGBAFloat, false);

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    float height = m_HeightMapInterpolated[x, y] / 200;
                    InitialState.SetPixel(x, y, new Color(height, 0, 0, 0.0f));
                }
            }
            InitialState.Apply();

            Settings = new SimulationSettings();

            /* ========= Setup computation =========== */
            // If there are already existing textures - release them
            if (_stateTexture != null)
                _stateTexture.Release();

            if (_waterFluxTexture != null)
                _waterFluxTexture.Release();

            if (_velocityTexture != null)
                _velocityTexture.Release();

            // Initialize texture for storing height map
            _stateTexture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            _stateTextureBuffer = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat)
            {
                enableRandomWrite = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Initialize texture for storing flow
            _waterFluxTexture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            // Initialize texture for storing flow for thermal erosion
            _terrainFluxTexture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };

            // Velocity texture
            _velocityTexture = new RenderTexture(Width, Height, 0, RenderTextureFormat.RGFloat)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            if (!_stateTexture.IsCreated())
                _stateTexture.Create();

            if (!_waterFluxTexture.IsCreated())
                _waterFluxTexture.Create();

            if (!_terrainFluxTexture.IsCreated())
                _terrainFluxTexture.Create();

            if (!_velocityTexture.IsCreated())
                _velocityTexture.Create();

            if (InitialState != null)
            {
                if (InitHeightMap != null)
                    Graphics.Blit(InitialState, _stateTexture, InitHeightMap);
                else
                    Graphics.Blit(InitialState, _stateTexture);
            }

            // Setup computation shader

            _kernels = new int[_kernelNames.Length];
            var i = 0;
            foreach (var kernelName in _kernelNames)
            {
                var kernel = erosionShader.FindKernel(kernelName); ;
                _kernels[i++] = kernel;

                // Set all textures
                erosionShader.SetTexture(kernel, "HeightMap", _stateTexture);
                erosionShader.SetTexture(kernel, "VelocityMap", _velocityTexture);
                erosionShader.SetTexture(kernel, "FluxMap", _waterFluxTexture);
                erosionShader.SetTexture(kernel, "TerrainFluxMap", _terrainFluxTexture);
            }

            erosionShader.SetInt("_Width", Width);
            erosionShader.SetInt("_Height", Height);
            erosionShader.GetKernelThreadGroupSizes(_kernels[0], out _threadsPerGroupX, out _threadsPerGroupY, out _threadsPerGroupZ);
        }

        public void erodeChunk(int timesteps)
        {
            int res = m_HeightMapInterpolated.GetLength(0);

            // Compute dispatch

            // General parameters
            erosionShader.SetFloat("_TimeDelta", Time.fixedDeltaTime * Settings.TimeScale);
            erosionShader.SetFloat("_PipeArea", Settings.PipeArea);
            erosionShader.SetFloat("_Gravity", Settings.Gravity);
            erosionShader.SetFloat("_PipeLength", Settings.PipeLength);
            erosionShader.SetVector("_CellSize", Settings.CellSize);
            erosionShader.SetFloat("_Evaporation", Settings.Evaporation);
            erosionShader.SetFloat("_RainRate", Settings.RainRate);

            // Hydraulic erosion
            erosionShader.SetFloat("_SedimentCapacity", Settings.SedimentCapacity);
            erosionShader.SetFloat("_MaxErosionDepth", Settings.MaximalErosionDepth);
            erosionShader.SetFloat("_SuspensionRate", Settings.SoilSuspensionRate);
            erosionShader.SetFloat("_DepositionRate", Settings.SedimentDepositionRate);
            erosionShader.SetFloat("_SedimentSofteningRate", Settings.SedimentSofteningRate);

            // Thermal erosion
            erosionShader.SetFloat("_ThermalErosionRate", Settings.ThermalErosionRate);
            erosionShader.SetFloat("_TalusAngleTangentCoeff", Settings.TalusAngleTangentCoeff);
            erosionShader.SetFloat("_TalusAngleTangentBias", Settings.TalusAngleTangentBias);
            erosionShader.SetFloat("_ThermalErosionTimeScale", Settings.ThermalErosionTimeScale);


            // Dispatch all passes sequentially

            foreach (var kernel in _kernels)
            {
                erosionShader.Dispatch(kernel,
                    _stateTexture.width / (int)_threadsPerGroupX,
                    _stateTexture.height / (int)_threadsPerGroupY, 1);
            }



            m_Landscape.renderTexture = _stateTexture;

            RenderTexture.active = _stateTexture;
            Texture2D tex = new Texture2D(_stateTexture.width, _stateTexture.height, TextureFormat.RGBAFloat, false);

            Rect rectReadPicture = new Rect(0, 0, _stateTexture.width, _stateTexture.height);
            tex.ReadPixels(rectReadPicture, 0, 0);

            RenderTexture.active = null;

            for (int i = 0; i < res; i++)
            {
                for (int j = 0; j < res; j++)
                {
                    m_HeightMapInterpolated[i, j] = tex.GetPixel(i, j).r * 200;
                    m_WaterHeightMap[i, j] = tex.GetPixel(i, j).g * 200;
                }
            }
        }
    }

}
