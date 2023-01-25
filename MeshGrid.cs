using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class MeshGrid
{
    GameObject parent;

    public GameObject meshObj;
    public Mesh mesh;
    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    //MeshCollider meshCollider;

    Vector2 position;
    float[,] m_Heightmap;
    public Vector3[] vertices;
    int[] triangles;
    Vector2[] uvs;

    float m_Size;

    Material ChunkMaterial;

    public bool is_working;


    public MeshGrid(GameObject _parent, Vector2 _position, float size, Shader ChunkShader)
    {
        position = _position;
        parent = _parent;
        m_Size = size;

        mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        meshObj = new GameObject("grid" + " " + position.x + " " + position.y);
        meshRenderer = meshObj.AddComponent<MeshRenderer>();
        ChunkMaterial = new Material(ChunkShader);
        meshRenderer.sharedMaterial = ChunkMaterial;
        meshFilter = meshObj.AddComponent<MeshFilter>();
        meshObj.transform.parent = parent.transform;
        meshObj.transform.position = new Vector3(position.x, 0, position.y);
    }

    public void UpdateMesh()
    {
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();

        meshFilter.mesh = mesh;
    }

    public void BuildFromWaterHeight(float[,] heightmap, float[,] waterHeight)
    {
        //res: die anzhal der Vertecies pro Seite
        //size: die größe des Meshes
        meshObj.name = "grid" + " " + position.x + " " + position.y + " Water";

        int res = heightmap.GetLength(0);
        m_Heightmap = new float[res, res];

        for (int x = 0; x < res; x++)
        {
            for (int y = 0; y < res; y++)
            {
                m_Heightmap[x, y] = heightmap[x, y] + waterHeight[x, y];
            }
        }

        vertices = new Vector3[(res + 1) * (res + 1)];
        List<int> triangles_List = new List<int>();
        uvs = new Vector2[(res + 1) * (res + 1)];

        float step_size = m_Size / (res - 1);

        for (int i = 0; i < res; i++)
        {
            for (int j = 0; j < res; j++)
            {
                float height = m_Heightmap[i, j];
                vertices[i * (res + 1) + j] = new Vector3(step_size * i, height, step_size * j);
                uvs[i * (res + 1) + j] = new Vector2(step_size * i, step_size * j);
            }
        }

        for (int i = 0; i < res - 1; i++)
        {
            for (int j = 0; j < res - 1; j++)
            {

                triangles_List.Add(i * (res + 1) + j);
                triangles_List.Add((i + 1) * (res + 1) + j + 1);
                triangles_List.Add((i + 1) * (res + 1) + j);

                triangles_List.Add((i * (res + 1)) + j);
                triangles_List.Add((i * (res + 1)) + j + 1);
                triangles_List.Add(((i + 1) * (res + 1)) + j + 1);
            }
        }

        triangles = triangles_List.ToArray();

        /*
        for (int x = 0; x < res; x++)
        {
            for (int y = 0; y < res; y++)
            {
                if (waterHeight[x, y] < 0.0001f)
                {
                    int index = y * (res + 1) + x;

                    for (int i = 0; i < triangles.Count / 3; i++)
                    {
                        if (triangles[i * 3 + 0] == index || triangles[i * 3 + 0] == index || triangles[i * 3 + 0] == index)
                        {
                            triangles.RemoveAt(i * 3 + 0);
                            triangles.RemoveAt(i * 3 + 1);
                            triangles.RemoveAt(i * 3 + 2);
                        }
                    }
                }
            }
        }*/

    }

    public void BuildFromHeightmap(float[,] heightmap)
    {
        is_working = true;

        //res: die anzhal der Vertecies pro Seite
        //size: die größe des Meshes
        int res = heightmap.GetLength(0);
        m_Heightmap = heightmap;

        /*
        var keywords = ChunkMaterial.shaderKeywords;

        //Debug.Log(keywords.Length);
        for (int i = 0; i < keywords.Length; i++)
        {
            Debug.Log(keywords[i]);
        }
        ChunkMaterial.SetTexture("_MaskStone", sandMask);*/

        vertices = new Vector3[(res + 1) * (res + 1)];
        List<int> triangles_List = new List<int>();
        uvs = new Vector2[(res + 1) * (res + 1)];

        float step_size = m_Size / (res - 1);

        for (int i = 0; i < res; i++)
        {
            for (int j = 0; j < res; j++)
            {
                float height = m_Heightmap[i, j];
                vertices[i * (res + 1) + j] = new Vector3(step_size * i, height, step_size * j);
                uvs[i * (res + 1) + j] = new Vector2(i / (res - 1f), j / (res - 1f));
            }
        }

        for (int i = 0; i < res - 1; i++)
        {
            for (int j = 0; j < res - 1; j++)
            {

                triangles_List.Add(i * (res + 1) + j);
                triangles_List.Add((i + 1) * (res + 1) + j + 1);
                triangles_List.Add((i + 1) * (res + 1) + j);

                triangles_List.Add((i * (res + 1)) + j);
                triangles_List.Add((i * (res + 1)) + j + 1);
                triangles_List.Add(((i + 1) * (res + 1)) + j + 1);
            }
        }

        triangles = triangles_List.ToArray();

        is_working = false;
    }
}