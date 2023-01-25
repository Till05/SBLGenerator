using System.Collections.Generic;
using System.IO;
using System;
using FluffyUnderware.Curvy;
using UnityEngine;

namespace LandscapeGenerator
{
    public class Landscape
    {
        public List<BezierSpline> m_LandscapeSplines = new List<BezierSpline>();

        public List<Vector3> Endpoints = new List<Vector3>();

        public List<Chunk> m_Chunks = new List<Chunk>();

        public List<MeshGrid> m_Meshes = new List<MeshGrid>();
        public List<MeshGrid> m_WaterMeshes = new List<MeshGrid>();

        //public Material LandscapeMaterial;
        public Shader WaterShader;

        public Shader LandscapeShader;

        public GameObject m_SplineParent;

        GameObject m_Self;
        GameObject m_Camera;

        public bool is_working;

        public ComputeShader interpolationShader;
        public ComputeShader diffusionShader;
        public ComputeShader erosionShader;
        public RenderTexture renderTexture;

        public Landscape(GameObject self, GameObject camera, GameObject splineParent)
        {
            m_SplineParent = splineParent;
            m_Self = self;
            m_Camera = camera;
        }

        public int CircularChunkArrangement(int renderDistance, float ChunkSize, int res, int resAmplifier)
        {
            for (int x = -renderDistance + 1; x < renderDistance; x++)
            {
                for (int y = -renderDistance + 1; y < renderDistance; y++)
                {
                    float distance = Mathf.Sqrt(x * x + y * y);
                    if (distance < renderDistance)
                    {
                        int resolution = Mathf.CeilToInt(res / (1 + distance));

                        Vector2 chunkPos = new Vector2(x * ChunkSize - ChunkSize / 2 + m_Camera.transform.position.x, y * ChunkSize - ChunkSize / 2 + m_Camera.transform.position.z);

                        MeshGrid chunk = new MeshGrid(m_Self, chunkPos, ChunkSize, LandscapeShader);
                        MeshGrid waterChunk = new MeshGrid(m_Self, chunkPos, ChunkSize, WaterShader);
                        Chunk landscapeChunk = new Chunk(resolution, ChunkSize, chunkPos.x, chunkPos.y, this);

                        landscapeChunk.interpolationShader = interpolationShader;
                        landscapeChunk.diffusionShader = diffusionShader;
                        landscapeChunk.erosionShader = erosionShader;

                        m_Meshes.Add(chunk);
                        m_Chunks.Add(landscapeChunk);
                        m_WaterMeshes.Add(waterChunk);
                    }
                }
            }

            return m_Chunks.Count;
        }

        public void SquareChunkArrangement(int renderDistance, float ChunkSize, int res, int resAmplifier)
        {
            int ChunkGridRes = 2 * renderDistance - 1;
            for (int x = -renderDistance + 1; x < renderDistance; x++)
            {
                for (int y = -renderDistance + 1; y < renderDistance; y++)
                {
                    float distance = Mathf.Sqrt(x * x + y * y);

                    int resolution = Mathf.CeilToInt(res / (1 + distance));
                    //int resolution = res;
                    Vector2 chunkPos = new Vector2(x * ChunkSize - ChunkSize / 2 + m_Camera.transform.position.x, y * ChunkSize - ChunkSize / 2 + m_Camera.transform.position.z);

                    MeshGrid chunk = new MeshGrid(m_Self, chunkPos, ChunkSize, LandscapeShader);
                    MeshGrid waterChunk = new MeshGrid(m_Self, chunkPos, ChunkSize, WaterShader);
                    Chunk landscapeChunk = new Chunk(resolution, ChunkSize, chunkPos.x, chunkPos.y, this);

                    landscapeChunk.m_LandscapeSplines = m_LandscapeSplines;
                    int grid_X = x + renderDistance - 1;
                    int grid_Y = y + renderDistance - 1;

                    landscapeChunk.m_NeighborChunks[0] = Noise.mod(grid_X - 1, ChunkGridRes) * ChunkGridRes + grid_Y;
                    landscapeChunk.m_NeighborChunks[1] = Noise.mod(grid_X - 1, ChunkGridRes) * ChunkGridRes + Noise.mod(grid_Y - 1, ChunkGridRes);

                    landscapeChunk.m_NeighborChunks[2] = grid_X * ChunkGridRes + Noise.mod(grid_Y - 1, ChunkGridRes);
                    landscapeChunk.m_NeighborChunks[3] = Noise.mod(grid_X + 1, ChunkGridRes) * ChunkGridRes + Noise.mod(grid_Y - 1, ChunkGridRes);

                    landscapeChunk.m_NeighborChunks[4] = Noise.mod(grid_X + 1, ChunkGridRes) * ChunkGridRes + grid_Y;
                    landscapeChunk.m_NeighborChunks[5] = Noise.mod(grid_X + 1, ChunkGridRes) * ChunkGridRes + Noise.mod(grid_Y + 1, ChunkGridRes);

                    landscapeChunk.m_NeighborChunks[6] = grid_X * ChunkGridRes + Noise.mod(grid_Y + 1, ChunkGridRes);
                    landscapeChunk.m_NeighborChunks[7] = Noise.mod(grid_X - 1, ChunkGridRes) * ChunkGridRes + Noise.mod(grid_Y + 1, ChunkGridRes);

                    /*
                    Debug.Log(x + " " + y);

                    for (int i = 0; i < 8; i++)
                    {
                        Debug.Log(landscapeChunk.m_NeighborChunks[i]);
                    }*/

                    m_Meshes.Add(chunk);
                    m_Chunks.Add(landscapeChunk);
                    m_WaterMeshes.Add(waterChunk);

                }
            }
        }

        public void makeLandscapeSplines()
        {
            List<BezierSpline> splines = new List<BezierSpline>();

            for (int i = 0; i < m_SplineParent.transform.childCount; i++)
            {
                CurvySpline curvySpline = m_SplineParent.transform.GetChild(i).GetComponent<CurvySpline>();

                BezierSpline bezierSpline = new BezierSpline(curvySpline);
                splines.Add(bezierSpline);
                Endpoints.Add(bezierSpline.m_Points[0]);
                Endpoints.Add(bezierSpline.m_Points[bezierSpline.m_Points.Count - 1]);
                bezierSpline.doPrecalcs();
            }

            m_LandscapeSplines = splines;
        }

        public void GenerateChunks(GenSettings genSettings)
        {
            for (int i = 0; i < m_Chunks.Count; i++)
            {
                m_Chunks[i].m_LandscapeSplines = m_LandscapeSplines;
                m_Chunks[i].GenerateChunk(genSettings);
                m_Chunks[i].GenerateInterpolation();
                //m_Chunks[i].GenerateInterpolationGPU();
            }
        }

        public void GenerateMeshes()
        {
            for (int i = 0; i < m_Meshes.Count; i++)
            {
                m_Meshes[i].BuildFromHeightmap(m_Chunks[i].m_HeightMapInterpolated);

                //byte[] bytes = m_Chunks[i].sandTexture.EncodeToPNG();
                //File.WriteAllBytes(Application.dataPath + "/Textures/Maps/SandMap" + i + ".png", bytes);
            }
        }

        public void UpdateMeshes()
        {
            for (int i = 0; i < m_Meshes.Count; i++)
            {
                if (m_Meshes[i].is_working == false)
                {
                    //Debug.Log("Updated Mesh");
                    m_Meshes[i].UpdateMesh();
                }
            }
        }

        public void UpdateChunks(GenSettings genSettings, CurvySplineEventArgs Args)
        {
            is_working = true;
            GenerateChunks(genSettings);
            GenerateMeshes();
            is_working = false;
        }

        public void ErodeLandscape(int timesteps)
        {

            for (int t = 0; t < timesteps; t++)
            {
                for (int i = 0; i < m_Chunks.Count; i++)
                {
                    //m_Chunks[i].CalculateSedimentMovement();
                    m_Chunks[i].erodeChunk(timesteps);
                }

                /*
                for (int i = 0; i < m_Chunks.Count; i++)
                {
                    m_Chunks[i].ApplySedimentMovement();
                    m_Chunks[i].ApplyErosion();
                }*/
            }

            m_WaterMeshes[0].BuildFromWaterHeight(m_Chunks[0].m_HeightMapInterpolated, m_Chunks[0].m_WaterHeightMap);
            m_WaterMeshes[0].UpdateMesh();

            /*
            for (int i = 0; i < m_Chunks.Count; i++)
            {
                for (int x = 0; x < m_Chunks[i].m_HeightMapInterpolated.GetLength(0); x++)
                {
                    for (int y = 0; y < m_Chunks[i].m_HeightMapInterpolated.GetLength(0); y++)
                    {
                        //Debug.Log(new Vector2(x, y) + "/////////////////////////////////");
                        //Debug.Log(m_Chunks[i].SedimentAmount[x, y]);
                        //Debug.Log(m_Chunks[i].velocity[x, y]);
                        //Vector3 currentPos = new Vector3(x * m_Chunks[i].m_Size / (m_Chunks[i].m_Res - 1), m_Chunks[i].m_HeightMapInterpolated[x, y], y * m_Chunks[i].m_Size / (m_Chunks[i].m_Res - 1));
                        //Debug.DrawRay(currentPos + new Vector3(m_Chunks[i].m_Pos.x, 0, m_Chunks[i].m_Pos.y), new Vector3(m_Chunks[i].velocity[x, y].x, 0, m_Chunks[i].velocity[x, y].y) * 10, Color.red, 1);
                        //Debug.Log(m_Chunks[i].waterHeight[x, y]);
                    }
                }
            }*/
        }
    }
}
