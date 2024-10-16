// Copyright (c) Mixed Reality Toolkit Contributors
// Licensed under the BSD 3-Clause

using MixedReality.Toolkit.UX;
using UnityEditor;
using UnityEngine;

namespace MixedReality.Toolkit.Editor
{
    /// <summary>
    /// A custom Unity editor for the <see cref="PressableButton"/> class.
    /// </summary>
    [CustomEditor(typeof(PressableButton), true)]
    [CanEditMultipleObjects]
    public class PressableButtonEditor : StatefulInteractableEditor
    {
        // Struct used to store state of preview.
        // This lets us display accurate info while button is being pressed.
        // All vectors / distances are in local space.
        private struct ButtonInfo
        {
            public PressableButton PressableButton;
            public Vector3 LocalCenter;
            public Vector2 PlaneExtents;

            // The actual values that the button uses
            public float StartPushPlane;
            public float EndPushPlane;
        }

        const string EditingEnabledKey = "MRTK_PressableButtonInspector_EditingEnabledKey";
        const string VisiblePlanesKey = "MRTK_PressableButtonInspector_VisiblePlanesKey";
        private static bool EditingEnabled = false;
        private static bool VisiblePlanes = true;

        private const float labelMouseOverDistance = 0.025f;

        private static GUIStyle labelStyle;

        private PressableButton[] buttons;

        private ButtonInfo[] currentInfos;

        private SerializedProperty distanceSpaceMode;
        private SerializedProperty startPushPlane;
        private SerializedProperty endPushPlane;

        private SerializedProperty smoothSelectionProgress;
        private SerializedProperty returnSpeed;
        private SerializedProperty extendSpeed;
        private SerializedProperty enforceFrontPush;
        private SerializedProperty rejectXYRollOff;
        private SerializedProperty rollOffXYDepth;
        private SerializedProperty rejectZRollOff;
        private SerializedProperty IsProximityHovered;

        private static readonly Vector3[] startPlaneVertices = new Vector3[4];
        private static readonly Vector3[] endPlaneVertices = new Vector3[4];

        /// <inheritdoc/>
        protected override void OnEnable()
        {
            base.OnEnable();

            buttons = new PressableButton[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                buttons[i] = (PressableButton)targets[i];
            }

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle();
                labelStyle.normal.textColor = Color.white;
            }

            distanceSpaceMode = serializedObject.FindProperty("distanceSpaceMode");
            startPushPlane = serializedObject.FindProperty("startPushPlane");
            endPushPlane = serializedObject.FindProperty("endPushPlane");

            smoothSelectionProgress = serializedObject.FindProperty("smoothSelectionProgress");
            extendSpeed = serializedObject.FindProperty("extendSpeed");
            returnSpeed = serializedObject.FindProperty("returnSpeed");

            enforceFrontPush = serializedObject.FindProperty("enforceFrontPush");
            rejectXYRollOff = serializedObject.FindProperty("rejectXYRollOff");
            rollOffXYDepth = serializedObject.FindProperty("rollOffXYDepth");
            rejectZRollOff = serializedObject.FindProperty("rejectZRollOff");

            IsProximityHovered = SetUpAutoProperty(nameof(IsProximityHovered));
        }

        [DrawGizmo(GizmoType.Selected)]
        private void OnSceneGUI()
        {
            if (!VisiblePlanes)
            {
                return;
            }

            currentInfos = GatherCurrentInfo();
            DrawButtonsInfo(currentInfos, EditingEnabled);
        }

        private ButtonInfo[] GatherCurrentInfo()
        {
            ButtonInfo[] result = new ButtonInfo[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                BoxCollider collider = buttons[i].GetComponentInChildren<BoxCollider>();
                result[i] = new ButtonInfo
                {
                    PressableButton = buttons[i],
                    // null coalesce safe as we're checking it in the same frame as we get it!
                    LocalCenter = collider?.center ?? Vector3.zero,
                    PlaneExtents = collider?.size ?? Vector3.zero,
                    StartPushPlane = buttons[i].StartPushPlane,
                    EndPushPlane = buttons[i].EndPushPlane
                };
            }
            return result;
        }

        private void DrawButtonsInfo(ButtonInfo[] info, bool editingEnabled)
        {
            if (editingEnabled)
            {
                EditorGUI.BeginChangeCheck();
            }

            var targetBehaviour = (MonoBehaviour)target;
            bool isOpaque = targetBehaviour.isActiveAndEnabled;
            float alpha = (isOpaque) ? 1.0f : 0.5f;

            for (int i = 0; i < info.Length; i++)
            {
                // START PUSH
                Handles.color = ApplyAlpha(Color.cyan, alpha);
                float newStartPushDistance = DrawPlaneAndHandle(startPlaneVertices, info[i].PlaneExtents * 0.5f, info[i].StartPushPlane, info[i], "Start Push Plane", editingEnabled);
                if (editingEnabled && newStartPushDistance != info[i].StartPushPlane)
                {
                    // Set the same value for all selected buttons
                    for (int j = 0; j < info.Length; j++)
                    {
                        info[j].StartPushPlane = Mathf.Min(newStartPushDistance, info[i].EndPushPlane);
                        // Maybe the button had lower EndPushPlane than the manually modified button, so we need to update its own EndPushPlane
                        if (info[j].StartPushPlane > info[j].EndPushPlane)
                        {
                            info[j].EndPushPlane = info[j].StartPushPlane;
                        }
                    }
                }

                // MAX PUSH
                var purple = new Color(0.28f, 0.0f, 0.69f);
                Handles.color = ApplyAlpha(purple, alpha);
                float newMaxPushDistance = DrawPlaneAndHandle(endPlaneVertices, info[i].PlaneExtents * 0.5f, info[i].EndPushPlane, info[i], "End Push Plane", editingEnabled);
                if (editingEnabled && newMaxPushDistance != info[i].EndPushPlane)
                {
                    // Set the same value for all selected buttons
                    for (int j = 0; j < info.Length; j++)
                    {
                        info[j].EndPushPlane = Mathf.Max(newMaxPushDistance, info[i].StartPushPlane);
                        // The button can have higher StartPushPlane than the manually modified button, so we need to update its own StartPushPlane
                        if (info[j].EndPushPlane < info[j].StartPushPlane)
                        {
                            info[j].StartPushPlane = info[j].EndPushPlane;
                        }
                    }
                }

                // Draw dotted lines showing path from beginning to end of button path
                Handles.color = Color.Lerp(Color.cyan, Color.clear, 0.25f);
                Handles.DrawDottedLine(startPlaneVertices[0], endPlaneVertices[0], 2.5f);
                Handles.DrawDottedLine(startPlaneVertices[1], endPlaneVertices[1], 2.5f);
                Handles.DrawDottedLine(startPlaneVertices[2], endPlaneVertices[2], 2.5f);
                Handles.DrawDottedLine(startPlaneVertices[3], endPlaneVertices[3], 2.5f);
            }

            if (editingEnabled && EditorGUI.EndChangeCheck())
            {
                if (buttons.Length == 1)
                {
                    Undo.RecordObject(buttons[0], string.Concat("Modify Button Planes of ", buttons[0].name));
                }
                else
                {
                    Undo.RecordObjects(buttons, "Modify Button Planes of multiple PressableButton");
                }

                for (int i = 0; i < info.Length; i++)
                {
                    buttons[i].StartPushPlane = info[i].StartPushPlane;
                    buttons[i].EndPushPlane = info[i].EndPushPlane;
                }
            }
        }

        private float DrawPlaneAndHandle(Vector3[] vertices, Vector2 halfExtents, float distance, ButtonInfo info, string label, bool editingEnabled)
        {
            Transform transform = info.PressableButton.transform;
            Vector3 centerWorld = transform.TransformPoint(new Vector3(info.LocalCenter.x, info.LocalCenter.y, info.PressableButton.GetLocalPositionAlongPushDirection(distance).z));
            MakeQuadFromPoint(vertices, centerWorld, halfExtents, info);

            if (VisiblePlanes)
            {
                Handles.DrawSolidRectangleWithOutline(vertices, Color.Lerp(Handles.color, Color.clear, 0.65f), Handles.color);
            }

            // Label
            {
                var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
                var dist = HandleUtility.DistancePointLine(vertices[1], ray.origin, ray.origin + ray.direction * 100.0f);

                if (dist < labelMouseOverDistance)
                {
                    DrawLabel(vertices[1], transform.up - transform.right, label, labelStyle);
                    HandleUtility.Repaint();
                }
            }

            // Draw forward / backward arrows so people know they can drag
            if (editingEnabled)
            {
                float handleSize = HandleUtility.GetHandleSize(vertices[1]) * 0.15f;

                Vector3 planeNormal = info.PressableButton.transform.forward;
                Handles.ArrowHandleCap(0, vertices[1], Quaternion.LookRotation(planeNormal), handleSize * 2, EventType.Repaint);
                Handles.ArrowHandleCap(0, vertices[1], Quaternion.LookRotation(-planeNormal), handleSize * 2, EventType.Repaint);

#if UNITY_2022_1_OR_NEWER
                Vector3 newPosition = Handles.FreeMoveHandle(vertices[1], handleSize, Vector3.zero, Handles.SphereHandleCap);
#else
                Vector3 newPosition = Handles.FreeMoveHandle(vertices[1], Quaternion.identity, handleSize, Vector3.zero, Handles.SphereHandleCap);
#endif

                if (!newPosition.Equals(vertices[1]))
                {
                    distance = info.PressableButton.GetDistanceAlongPushDirection(newPosition);
                }
            }

            return distance;
        }

        static bool advancedButtonFoldout = false;
        static bool editorFoldout = false;

        /// <inheritdoc />
        protected override void DrawMRTKInteractableFlags()
        {
            Color previousGUIColor = GUI.color;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                TimedFlag[] pressableButtons = new TimedFlag[targets.Length];
                for (int i = 0; i < targets.Length; i++)
                {
                    pressableButtons[i] = ((PressableButton)targets[i]).IsProximityHovered;
                }
                EditorGUILayout.LabelField("PressableButton Events", EditorStyles.boldLabel);
                EditorGUILayout.Space();
                DrawTimedFlags(IsProximityHovered, pressableButtons, previousGUIColor, Color.cyan);
            }

            EditorGUILayout.Space();

            base.DrawMRTKInteractableFlags();
        }

        /// <inheritdoc />
        protected override void DrawProperties()
        {
            base.DrawProperties();

            if (distanceSpaceMode == null) { return; }

            serializedObject.Update();

            advancedButtonFoldout = EditorGUILayout.Foldout(advancedButtonFoldout, EditorGUIUtility.TrTempContent("Volumetric Press Settings"), true, EditorStyles.foldoutHeader);
            if (advancedButtonFoldout)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var currentMode = distanceSpaceMode.intValue;
                    EditorGUILayout.PropertyField(distanceSpaceMode);

                    // EndChangeCheck returns true when something was selected in the dropdown, but
                    // doesn't necessarily mean that the value itself changed. Check for that too.
                    if (EditorGUI.EndChangeCheck() && currentMode != distanceSpaceMode.intValue)
                    {
                        // Changing the DistanceSpaceMode requires updating the plane distance values so they stay in the same relative ratio positions
                        if (buttons.Length == 1)
                        {
                            Undo.RecordObject(buttons[0], string.Concat("Trigger Plane Distance Conversion of ", buttons[0].name));
                        }
                        else
                        {
                            Undo.RecordObjects(buttons, "Trigger Plane Distance Conversion of multiple PressableButton");
                        }
                        foreach (PressableButton button in buttons)
                        {
                            button.DistanceSpaceMode = (PressableButton.SpaceMode)distanceSpaceMode.intValue;
                        }
                        serializedObject.Update();
                    }

                    // Push settings
                    EditorGUILayout.PropertyField(startPushPlane);
                    EditorGUILayout.PropertyField(endPushPlane);

                    // Other settings
                    EditorGUILayout.PropertyField(smoothSelectionProgress);
                    EditorGUILayout.PropertyField(extendSpeed);
                    EditorGUILayout.PropertyField(returnSpeed);

                    // Roll-off rejection
                    EditorGUILayout.PropertyField(enforceFrontPush);
                    EditorGUILayout.PropertyField(rejectXYRollOff);
                    if (rejectXYRollOff.boolValue)
                    {
                        EditorGUILayout.PropertyField(rollOffXYDepth);
                    }
                    EditorGUILayout.PropertyField(rejectZRollOff);
                }
            }

            // editor settings
            {
                EditorGUI.BeginDisabledGroup(Application.isPlaying);
                editorFoldout = EditorGUILayout.Foldout(editorFoldout, EditorGUIUtility.TrTempContent("Button Editor Settings"), true, EditorStyles.foldoutHeader);
                if (editorFoldout)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        var prevVisiblePlanes = SessionState.GetBool(VisiblePlanesKey, true);
                        VisiblePlanes = EditorGUILayout.Toggle("Show Button Event Planes", prevVisiblePlanes);
                        if (VisiblePlanes != prevVisiblePlanes)
                        {
                            SessionState.SetBool(VisiblePlanesKey, VisiblePlanes);
                            EditorUtility.SetDirty(target);
                        }

                        // enable plane editing
                        {
                            EditorGUI.BeginDisabledGroup(VisiblePlanes == false);
                            var prevEditingEnabled = SessionState.GetBool(EditingEnabledKey, false);
                            EditingEnabled = EditorGUILayout.Toggle("Make Planes Editable", EditingEnabled);
                            if (EditingEnabled != prevEditingEnabled)
                            {
                                SessionState.SetBool(EditingEnabledKey, EditingEnabled);
                                EditorUtility.SetDirty(target);
                            }
                            EditorGUI.EndDisabledGroup();
                        }
                    }
                }
                EditorGUI.EndDisabledGroup();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawLabel(Vector3 origin, Vector3 direction, string content, GUIStyle labelStyle)
        {
            Color colorOnEnter = Handles.color;

            float handleSize = HandleUtility.GetHandleSize(origin);
            Vector3 handlePos = origin + (2 * handleSize * direction.normalized);
            Handles.Label(handlePos + (0.1f * handleSize * Vector3.up), content, labelStyle);
            Handles.color = Color.Lerp(colorOnEnter, Color.clear, 0.25f);
            Handles.DrawDottedLine(origin, handlePos, 5f);

            Handles.color = colorOnEnter;
        }

        private void MakeQuadFromPoint(Vector3[] vertices, Vector3 centerWorld, Vector2 halfExtents, ButtonInfo info)
        {
            Transform transform = info.PressableButton.transform;
            vertices[0] = transform.TransformVector((new Vector3(-halfExtents.x, -halfExtents.y, 0.0f))) + centerWorld;
            vertices[1] = transform.TransformVector((new Vector3(-halfExtents.x, +halfExtents.y, 0.0f))) + centerWorld;
            vertices[2] = transform.TransformVector((new Vector3(+halfExtents.x, +halfExtents.y, 0.0f))) + centerWorld;
            vertices[3] = transform.TransformVector((new Vector3(+halfExtents.x, -halfExtents.y, 0.0f))) + centerWorld;
        }

        private static Color ApplyAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, color.a * alpha);
        }
    }
}
