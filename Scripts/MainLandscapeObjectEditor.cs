using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.EditorTools;

[CustomEditor(typeof(MainLandscapeObject))]
public class MainLandscapeObjectEditor : Editor
{
    SerializedProperty self;
    SerializedProperty Camera;
    SerializedProperty SplineParent;
    SerializedProperty LandscapeShader;
    SerializedProperty WaterShader;

    SerializedProperty RenderDistance;
    SerializedProperty ChunkSize;
    SerializedProperty Resolution;
    SerializedProperty ResolutionAmplifier;
    SerializedProperty NumberOfSamples;
    SerializedProperty generateLandscape;
    SerializedProperty interpolationShader;
    SerializedProperty diffusionShader;
    SerializedProperty renderTexture;
    SerializedProperty erosionShader;


    void OnEnable()
    {
        self = serializedObject.FindProperty("self");
        Camera = serializedObject.FindProperty("Camera");
        SplineParent = serializedObject.FindProperty("m_SplineParent");
        WaterShader = serializedObject.FindProperty("WaterShader");
        LandscapeShader = serializedObject.FindProperty("LandscapeShader");

        RenderDistance = serializedObject.FindProperty("m_RenderDistance");
        ChunkSize = serializedObject.FindProperty("m_ChunkSize");
        Resolution = serializedObject.FindProperty("m_Res");
        ResolutionAmplifier = serializedObject.FindProperty("m_resAmplifier");
        NumberOfSamples = serializedObject.FindProperty("sampels_number");
        interpolationShader = serializedObject.FindProperty("interpolationShader");
        diffusionShader = serializedObject.FindProperty("diffusionShader");
        erosionShader = serializedObject.FindProperty("erosionShader");
        renderTexture = serializedObject.FindProperty("renderTexture");

        generateLandscape = serializedObject.FindProperty("generateLandscape");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(self);
        EditorGUILayout.PropertyField(Camera);
        EditorGUILayout.PropertyField(SplineParent);

        EditorGUILayout.Separator();

        EditorGUILayout.PropertyField(interpolationShader);
        EditorGUILayout.PropertyField(diffusionShader);
        EditorGUILayout.PropertyField(erosionShader);
        EditorGUILayout.PropertyField(renderTexture);
        EditorGUILayout.PropertyField(WaterShader);
        EditorGUILayout.PropertyField(LandscapeShader);

        EditorGUILayout.Separator();

        EditorGUILayout.PropertyField(RenderDistance);
        EditorGUILayout.PropertyField(ChunkSize);
        EditorGUILayout.PropertyField(Resolution);
        EditorGUILayout.PropertyField(ResolutionAmplifier);
        EditorGUILayout.PropertyField(NumberOfSamples);

        if (GUILayout.Button("Update Landscape"))
        {
            generateLandscape.boolValue = true;
        }

        serializedObject.ApplyModifiedProperties();
    }
}
