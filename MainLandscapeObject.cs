using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LandscapeGenerator;
using FluffyUnderware.Curvy;
using System.Threading;
using UnityEngine.Events;

public class MainLandscapeObject : MonoBehaviour
{
    //List<MeshGrid> ChunkObjects = new List<MeshGrid>();

    List<Vector3> Endpoints = new List<Vector3>();

    public GameObject self;
    public GameObject Camera;
    public GameObject m_SplineParent;

    //public Material LandscapeMaterial;
    public Shader WaterShader;
    public Shader LandscapeShader;

    public int m_RenderDistance = 5;
    public float m_ChunkSize = 10f;

    public int m_Res = 10;
    public int m_resAmplifier = 3;
    GenSettings m_genSettings;

    public int sampels_number = 10;

    Landscape landscape;
    public bool generateLandscape = false;

    Thread m_LandscapeThread;

    CurvySplineEventArgs m_EventArgs;

    public ComputeShader interpolationShader;
    public ComputeShader diffusionShader;
    public ComputeShader erosionShader;
    public RenderTexture renderTexture;

    // Start is called before the first frame update
    void Start()
    {
        landscape = new Landscape(self, Camera, m_SplineParent);
        landscape.LandscapeShader = LandscapeShader;
        landscape.WaterShader = WaterShader;
        landscape.interpolationShader = interpolationShader;
        landscape.diffusionShader = diffusionShader;
        landscape.erosionShader = erosionShader;

        for (int i = 0; i < m_SplineParent.transform.childCount; i++)
        {
            CurvySpline curvySpline = m_SplineParent.transform.GetChild(i).GetComponent<CurvySpline>();
            curvySpline.OnRefresh.AddListener(OnSplinesChanged);
        }

        m_genSettings = new GenSettings();

        m_genSettings.sampels_number = sampels_number;
        m_genSettings.lacunarity = 2f;
        m_genSettings.noise_strength = 2f;
        m_genSettings.scale = 0.1f;
        m_genSettings.numOctaves = 8;
        m_genSettings.persistence = 0.5f;
        m_genSettings.resAmplifier = m_resAmplifier;

        landscape.makeLandscapeSplines();
        landscape.CircularChunkArrangement(m_RenderDistance, m_ChunkSize, m_Res, m_resAmplifier);
        //landscape.SquareChunkArrangement(m_RenderDistance, m_ChunkSize, m_Res, m_resAmplifier);
        landscape.GenerateChunks(m_genSettings);
        landscape.m_Chunks[0].InitialiseErosion();
        //landscape.ErodeLandscape(30);
        landscape.GenerateMeshes();
        landscape.UpdateMeshes();

        

        //landscape.GenerateMeshes();
    }

    // Update is called once per frame
    void Update()
    {
        /*
        if (generateLandscape)
        {
            //Debug.Log("build Landscape");
            landscape.makeLandscapeSplines();
            RebuildLandscape();
            generateLandscape = false;
        }*/
        //landscape.UpdateMeshes();
        //Debug.Log(0);
        landscape.ErodeLandscape(1);
        renderTexture = landscape.renderTexture;
        //landscape.GenerateChunks(m_genSettings);

        landscape.GenerateMeshes();
        landscape.UpdateMeshes();
    }

    public void OnSplinesChanged(CurvySplineEventArgs stuff)
    {
        generateLandscape = true;
        
        m_EventArgs = stuff;
        //Debug.Log("Splines Changed");
    }

    void RebuildLandscape()
    {
        if (m_LandscapeThread != null)
        {
            //Debug.Log("thing");
            if (m_LandscapeThread.IsAlive)
            {

            }
            else
            {
                m_LandscapeThread = new Thread(() => landscape.UpdateChunks(m_genSettings, m_EventArgs));
                m_LandscapeThread.Start();
            }
        }
        else
        {
            //Debug.Log("other thing");
            m_LandscapeThread = new Thread(() => landscape.UpdateChunks(m_genSettings, m_EventArgs));
            m_LandscapeThread.Start();
        }
    }
}
