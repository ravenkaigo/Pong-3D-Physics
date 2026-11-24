using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;

[InitializeOnLoad]
public class PongSceneBuilder : EditorWindow
{
    private const string AutoBuildKey = "Pong3D_HasAutoBuilt_v8";

    static PongSceneBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!SessionState.GetBool(AutoBuildKey, false))
            {
                SessionState.SetBool(AutoBuildKey, true);
                BuildScene();
                PhysicsSelfCheck.Run();
            }
        };
    }

    [MenuItem("Pong3D/Build Full ITTF Rules Scene")]
    public static void BuildScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[PongSceneBuilder] Cannot build scene while in Play mode. Stop Play mode first.");
            return;
        }

        EnsureTags("Ball", "AI", "Table", "Net", "Player");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        if (!Directory.Exists("Assets/Materials")) Directory.CreateDirectory("Assets/Materials");

        Material matTable = CreateOrGetMat("Assets/Materials/M_TableSurface.mat", new Color(0.08f, 0.22f, 0.36f), 0.3f);
        Material matLines = CreateOrGetMat("Assets/Materials/M_TableLines.mat", new Color(0.96f, 0.96f, 0.98f), 0.1f);
        Material matLegs = CreateOrGetMat("Assets/Materials/M_TableLegs.mat", new Color(0.05f, 0.08f, 0.12f), 0.2f);
        Material matFloor = CreateOrGetMat("Assets/Materials/M_ArenaFloor.mat", new Color(0.035f, 0.04f, 0.055f), 0.15f);
        Material matNet = CreateOrGetMat("Assets/Materials/M_Net.mat", new Color(0.92f, 0.92f, 0.92f, 0.75f), 0.1f, isTransparent: true);

        // Blade and rubbers are see-through so the player paddle never hides the incoming ball.
        Material matRed = CreateOrGetMat("Assets/Materials/M_PaddleRed.mat", new Color(0.88f, 0.14f, 0.18f, 0.4f), 0.45f, isTransparent: true);
        Material matBlack = CreateOrGetMat("Assets/Materials/M_PaddleBlack.mat", new Color(0.14f, 0.14f, 0.16f, 0.4f), 0.45f, isTransparent: true);
        Material matRim = CreateOrGetMat("Assets/Materials/M_PaddleRim.mat", new Color(0.25f, 0.18f, 0.12f, 0.35f), 0.2f, isTransparent: true);
        Material matHandle = CreateOrGetMat("Assets/Materials/M_PaddleHandle.mat", new Color(0.82f, 0.65f, 0.46f), 0.1f);

        // Emission keyword stays on (at black) so the heat glow's shader variant is kept in builds.
        Material matBall = CreateOrGetMat("Assets/Materials/M_Ball.mat", new Color(1.0f, 0.62f, 0.12f), 0.30f);
        matBall.color = new Color(1.0f, 0.62f, 0.12f);
        matBall.EnableKeyword("_EMISSION");
        matBall.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        if (matBall.HasProperty("_EmissionColor")) matBall.SetColor("_EmissionColor", Color.black);

        Material matTrail = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_BallTrail.mat");
        if (matTrail == null)
        {
            Shader s = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Standard");
            matTrail = new Material(s);
            matTrail.color = new Color(1.0f, 0.82f, 0.40f, 0.45f);
            AssetDatabase.CreateAsset(matTrail, "Assets/Materials/M_BallTrail.mat");
        }
        else
        {
            matTrail.color = new Color(1.0f, 0.82f, 0.40f, 0.45f);
        }

        PhysicsMaterial tablePhysMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/Materials/TablePhysMat.physicsMaterial");
        // Unity 6 refuses CreateAsset for .physicsMaterial; an in-scene material is enough since BallPhysics owns the bounce.
        if (tablePhysMat == null)
        {
            tablePhysMat = new PhysicsMaterial("TablePhysMat")
            {
                bounciness = 0.90f,
                dynamicFriction = 0.35f,
                staticFriction = 0.35f,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Average
            };
        }

        // Night arena: one shadow-casting spot makes the table the only lit area; the fill keeps paddles readable.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.05f, 0.06f, 0.09f);

        GameObject spotObj = new GameObject("Arena_Spot");
        Light spot = spotObj.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = 22f;
        spot.spotAngle = 100f;
        spot.innerSpotAngle = 70f;
        spot.intensity = 3.2f;
        spot.color = new Color(1f, 0.96f, 0.88f);
        spot.shadows = LightShadows.Soft;
        spot.shadowStrength = 0.85f;
        spotObj.transform.SetPositionAndRotation(new Vector3(0f, 10f, -0.5f), Quaternion.Euler(90f, 0f, 0f));

        GameObject fillObj = new GameObject("Arena_Fill");
        Light fill = fillObj.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.25f;
        fill.color = new Color(0.65f, 0.75f, 1f);
        fill.shadows = LightShadows.None;
        fillObj.transform.rotation = Quaternion.Euler(30f, 0f, 0f);

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Arena_Floor";
        floor.transform.position = new Vector3(0f, -4.2f, 0f);
        floor.transform.localScale = new Vector3(6f, 1f, 6f);
        floor.GetComponent<Renderer>().sharedMaterial = matFloor;
        Object.DestroyImmediate(floor.GetComponent<Collider>());

        GameObject camRig = new GameObject("Camera_Rig");
        camRig.transform.position = new Vector3(0f, 3.50f, -10.8f);
        camRig.transform.rotation = Quaternion.Euler(20f, 0f, 0f);
        CameraShake camShake = camRig.AddComponent<CameraShake>();

        GameObject camObj = new GameObject("Main Camera");
        camObj.transform.SetParent(camRig.transform, false);
        Camera cam = camObj.AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.fieldOfView = 60f;
        cam.backgroundColor = new Color(0.02f, 0.025f, 0.04f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        camObj.AddComponent<AudioListener>();

        // Table is scaled up for the phone camera: 16 m x 5.6 m, surface at y = 0.
        GameObject table = new GameObject("Table_Holder");
        table.tag = "Table";

        GameObject tableSurface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tableSurface.name = "Table_Surface";
        tableSurface.tag = "Table";
        tableSurface.transform.SetParent(table.transform, false);
        tableSurface.transform.localScale = new Vector3(5.6f, 0.4f, 16.0f);
        tableSurface.transform.position = new Vector3(0f, -0.2f, 0f);
        tableSurface.GetComponent<Renderer>().sharedMaterial = matTable;
        tableSurface.GetComponent<BoxCollider>().material = tablePhysMat;

        CreateLine("Line_Center", table.transform, new Vector3(0f, 0.01f, 0f), new Vector3(0.06f, 0.02f, 16f), matLines);
        CreateLine("Line_Border_L", table.transform, new Vector3(-2.75f, 0.01f, 0f), new Vector3(0.06f, 0.02f, 16f), matLines);
        CreateLine("Line_Border_R", table.transform, new Vector3(2.75f, 0.01f, 0f), new Vector3(0.06f, 0.02f, 16f), matLines);
        CreateLine("Line_Border_Bottom", table.transform, new Vector3(0f, 0.01f, -7.95f), new Vector3(5.6f, 0.02f, 0.06f), matLines);
        CreateLine("Line_Border_Top", table.transform, new Vector3(0f, 0.01f, 7.95f), new Vector3(5.6f, 0.02f, 0.06f), matLines);

        CreateLeg("Leg_BL", table.transform, new Vector3(-2.6f, -2.2f, -7.6f), matLegs);
        CreateLeg("Leg_BR", table.transform, new Vector3(2.6f, -2.2f, -7.6f), matLegs);
        CreateLeg("Leg_TL", table.transform, new Vector3(-2.6f, -2.2f, 7.6f), matLegs);
        CreateLeg("Leg_TR", table.transform, new Vector3(2.6f, -2.2f, 7.6f), matLegs);

        GameObject netObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        netObj.name = "Table_Net";
        netObj.tag = "Net";
        netObj.transform.SetParent(table.transform, false);
        netObj.transform.localPosition = new Vector3(0f, 0.08f, 0f);
        netObj.transform.localScale = new Vector3(5.8f, 0.16f, 0.04f);
        netObj.GetComponent<Renderer>().sharedMaterial = matNet;
        BoxCollider netCol = netObj.GetComponent<BoxCollider>();
        netCol.isTrigger = true; // BallController.HandleNetTouch decides the cord outcome.

        GameObject player = new GameObject("Player_Paddle");
        player.tag = "Player";
        player.transform.position = new Vector3(0.50f, 0.55f, -7.6f);
        player.transform.rotation = Quaternion.Euler(-6f, 0f, 0f);
        Rigidbody playerRb = player.AddComponent<Rigidbody>();
        playerRb.isKinematic = true;

        BoxCollider playerCol = player.AddComponent<BoxCollider>();
        playerCol.center = Vector3.zero;
        playerCol.size = new Vector3(1.20f, 1.20f, 0.20f);

        GameObject playerPivot = new GameObject("Visual_Pivot");
        playerPivot.transform.SetParent(player.transform, false);
        PaddleVisuals playerVis = playerPivot.AddComponent<PaddleVisuals>();

        GameObject playerRim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        playerRim.name = "Model_BladeRim";
        playerRim.transform.SetParent(playerPivot.transform, false);
        playerRim.transform.localScale = new Vector3(1.05f, 0.04f, 1.05f);
        playerRim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        playerRim.GetComponent<Renderer>().sharedMaterial = matRim;
        Object.DestroyImmediate(playerRim.GetComponent<Collider>());

        GameObject playerRedFace = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        playerRedFace.name = "Model_RedRubber";
        playerRedFace.transform.SetParent(playerPivot.transform, false);
        playerRedFace.transform.localScale = new Vector3(1.02f, 0.025f, 1.02f);
        playerRedFace.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        playerRedFace.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        playerRedFace.GetComponent<Renderer>().sharedMaterial = matRed;
        Object.DestroyImmediate(playerRedFace.GetComponent<Collider>());

        GameObject playerBlackFace = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        playerBlackFace.name = "Model_BlackRubber";
        playerBlackFace.transform.SetParent(playerPivot.transform, false);
        playerBlackFace.transform.localScale = new Vector3(1.02f, 0.025f, 1.02f);
        playerBlackFace.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        playerBlackFace.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        playerBlackFace.GetComponent<Renderer>().sharedMaterial = matBlack;
        Object.DestroyImmediate(playerBlackFace.GetComponent<Collider>());

        GameObject playerHandle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        playerHandle.name = "Model_RacketHandle";
        playerHandle.transform.SetParent(playerPivot.transform, false);
        playerHandle.transform.localScale = new Vector3(0.18f, 0.55f, 0.16f);
        playerHandle.transform.localPosition = new Vector3(0f, -0.60f, 0f);
        playerHandle.GetComponent<Renderer>().sharedMaterial = matHandle;
        Object.DestroyImmediate(playerHandle.GetComponent<Collider>());

        PlayerPaddleController playerCtrl = player.AddComponent<PlayerPaddleController>();
        SerializedObject soPlayer = new SerializedObject(playerCtrl);
        soPlayer.FindProperty("visuals").objectReferenceValue = playerVis;
        soPlayer.FindProperty("smoothTime").floatValue = 0.040f;
        soPlayer.FindProperty("maxPaddleSpeed").floatValue = 16.0f;
        soPlayer.FindProperty("strokeForwardDist").floatValue = 0.65f;
        soPlayer.ApplyModifiedProperties();

        SerializedObject soCam = new SerializedObject(camShake);
        soCam.FindProperty("targetPaddle").objectReferenceValue = player.transform;
        soCam.FindProperty("lateralTrackRatio").floatValue = 0.38f;
        soCam.FindProperty("maxCamYaw").floatValue = 4.5f;
        soCam.FindProperty("maxCamRoll").floatValue = 1.2f;
        soCam.ApplyModifiedProperties();

        GameObject ai = new GameObject("AI_Paddle");
        ai.tag = "AI";
        ai.transform.position = new Vector3(0f, 0.65f, 7.6f);
        Rigidbody aiRb = ai.AddComponent<Rigidbody>();
        aiRb.isKinematic = true;

        BoxCollider aiCol = ai.AddComponent<BoxCollider>();
        aiCol.center = Vector3.zero;
        aiCol.size = new Vector3(1.20f, 1.20f, 0.20f);

        GameObject aiPivot = new GameObject("Visual_Pivot");
        aiPivot.transform.SetParent(ai.transform, false);
        aiPivot.transform.localRotation = Quaternion.Euler(-10f, 0f, 0f);
        PaddleVisuals aiVis = aiPivot.AddComponent<PaddleVisuals>();

        GameObject aiRim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        aiRim.name = "Model_BladeRim";
        aiRim.transform.SetParent(aiPivot.transform, false);
        aiRim.transform.localScale = new Vector3(1.05f, 0.04f, 1.05f);
        aiRim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        aiRim.GetComponent<Renderer>().sharedMaterial = matRim;
        Object.DestroyImmediate(aiRim.GetComponent<Collider>());

        GameObject aiBlackFace = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        aiBlackFace.name = "Model_BlackRubber";
        aiBlackFace.transform.SetParent(aiPivot.transform, false);
        aiBlackFace.transform.localScale = new Vector3(1.02f, 0.025f, 1.02f);
        aiBlackFace.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        aiBlackFace.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        aiBlackFace.GetComponent<Renderer>().sharedMaterial = matBlack;
        Object.DestroyImmediate(aiBlackFace.GetComponent<Collider>());

        GameObject aiRedFace = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        aiRedFace.name = "Model_RedRubber";
        aiRedFace.transform.SetParent(aiPivot.transform, false);
        aiRedFace.transform.localScale = new Vector3(1.02f, 0.025f, 1.02f);
        aiRedFace.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        aiRedFace.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        aiRedFace.GetComponent<Renderer>().sharedMaterial = matRed;
        Object.DestroyImmediate(aiRedFace.GetComponent<Collider>());

        GameObject aiHandle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        aiHandle.name = "Model_RacketHandle";
        aiHandle.transform.SetParent(aiPivot.transform, false);
        aiHandle.transform.localScale = new Vector3(0.18f, 0.55f, 0.16f);
        aiHandle.transform.localPosition = new Vector3(0f, -0.60f, 0f);
        aiHandle.GetComponent<Renderer>().sharedMaterial = matHandle;
        Object.DestroyImmediate(aiHandle.GetComponent<Collider>());

        AIPaddleController aiCtrl = ai.AddComponent<AIPaddleController>();

        GameObject ball = new GameObject("Ball_Entity");
        ball.tag = "Ball";
        ball.transform.position = new Vector3(0.30f, 0.55f, -7.0f);

        Rigidbody ballRb = ball.AddComponent<Rigidbody>();
        ballRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ballRb.useGravity = false;
        ballRb.constraints = RigidbodyConstraints.FreezeRotation;

        SphereCollider ballCol = ball.AddComponent<SphereCollider>();
        ballCol.radius = 0.19f;

        GameObject ballPivot = new GameObject("Visual_Pivot");
        ballPivot.transform.SetParent(ball.transform, false);
        BallVisualFX ballVis = ballPivot.AddComponent<BallVisualFX>();

        GameObject ballModel = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ballModel.name = "Model_BallSphere";
        ballModel.transform.SetParent(ballPivot.transform, false);
        ballModel.transform.localScale = new Vector3(0.38f, 0.38f, 0.38f);
        ballModel.GetComponent<Renderer>().sharedMaterial = matBall;
        Object.DestroyImmediate(ballModel.GetComponent<Collider>());

        GameObject trailObj = new GameObject("FX_TrailRenderer");
        trailObj.transform.SetParent(ball.transform, false);
        TrailRenderer trail = trailObj.AddComponent<TrailRenderer>();
        trail.time = 0.12f;
        trail.startWidth = 0.09f;
        trail.endWidth = 0f;
        trail.material = matTrail;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0.0f), new GradientColorKey(new Color(1f, 0.60f, 0.20f), 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.40f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        trail.colorGradient = gradient;

        SerializedObject soBallVis = new SerializedObject(ballVis);
        soBallVis.FindProperty("trail").objectReferenceValue = trail;
        soBallVis.FindProperty("meshRenderer").objectReferenceValue = ballModel.GetComponent<Renderer>();
        soBallVis.ApplyModifiedProperties();

        BallController ballCtrl = ball.AddComponent<BallController>();
        SerializedObject soBall = new SerializedObject(ballCtrl);
        soBall.FindProperty("playerTransform").objectReferenceValue = player.transform;
        soBall.FindProperty("aiTransform").objectReferenceValue = ai.transform;
        soBall.FindProperty("visualFX").objectReferenceValue = ballVis;
        soBall.FindProperty("serveSpeed").floatValue = 7.6f;
        soBall.FindProperty("maxSpeed").floatValue = 17.0f;
        soBall.FindProperty("airDrag").floatValue = 0.012f;
        soBall.FindProperty("gravityScale").floatValue = 0.38f;
        soBall.FindProperty("tableBounceEfficiency").floatValue = 0.88f;
        soBall.ApplyModifiedProperties();

        SerializedObject soP = new SerializedObject(playerCtrl);
        soP.FindProperty("ballTransform").objectReferenceValue = ball.transform;
        soP.ApplyModifiedProperties();

        SerializedObject soAI = new SerializedObject(aiCtrl);
        soAI.FindProperty("ball").objectReferenceValue = ball.transform;
        soAI.FindProperty("visuals").objectReferenceValue = aiVis;
        soAI.ApplyModifiedProperties();

        GameObject triggers = new GameObject("Triggers");

        GameObject playerGoal = new GameObject("PlayerGoalZone");
        playerGoal.transform.SetParent(triggers.transform, false);
        playerGoal.transform.position = new Vector3(0f, 0.5f, -9.5f);
        BoxCollider pGoalCol = playerGoal.AddComponent<BoxCollider>();
        pGoalCol.isTrigger = true;
        pGoalCol.size = new Vector3(10f, 4f, 2f);
        GoalTrigger pGoal = playerGoal.AddComponent<GoalTrigger>();
        SerializedObject soPGoal = new SerializedObject(pGoal);
        soPGoal.FindProperty("isPlayerGoal").boolValue = true;
        soPGoal.ApplyModifiedProperties();

        GameObject aiGoal = new GameObject("AIGoalZone");
        aiGoal.transform.SetParent(triggers.transform, false);
        aiGoal.transform.position = new Vector3(0f, 0.5f, 9.5f);
        BoxCollider aiGoalCol = aiGoal.AddComponent<BoxCollider>();
        aiGoalCol.isTrigger = true;
        aiGoalCol.size = new Vector3(10f, 4f, 2f);
        GoalTrigger aGoal = aiGoal.AddComponent<GoalTrigger>();
        SerializedObject soAGoal = new SerializedObject(aGoal);
        soAGoal.FindProperty("isPlayerGoal").boolValue = false;
        soAGoal.ApplyModifiedProperties();

        GameObject managers = new GameObject("Managers");
        GameManager gm = managers.AddComponent<GameManager>();
        managers.AddComponent<HitStopManager>();

        SerializedObject soGM = new SerializedObject(gm);
        soGM.FindProperty("ball").objectReferenceValue = ballCtrl;
        soGM.FindProperty("aiServeDelay").floatValue = 0.65f;
        soGM.FindProperty("roundResetDelay").floatValue = 0.75f;
        soGM.FindProperty("playerServePos").vector3Value = new Vector3(0.30f, 0.55f, -7.0f);
        soGM.FindProperty("aiServePos").vector3Value = new Vector3(0f, 0.70f, 7.0f);
        soGM.ApplyModifiedProperties();

        if (!Directory.Exists("Assets/Scenes")) Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        AssetDatabase.Refresh();
        Debug.Log("Pong 3D Panoramic First-Person Scene successfully rebuilt: Assets/Scenes/Main.unity");
    }

    private static Material CreateOrGetMat(string path, Color baseColor, float smoothness, bool isTransparent = false, Color? emission = null)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader standardShader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Mobile/Diffuse");
            mat = new Material(standardShader);
            mat.color = baseColor;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

            if (isTransparent)
            {
                mat.SetFloat("_Mode", 3);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }

            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
            }

            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.color = baseColor;
            if (isTransparent)
            {
                mat.SetFloat("_Mode", 3);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
        }
        return mat;
    }

    private static void CreateLine(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.name = name;
        line.transform.SetParent(parent, false);
        line.transform.localPosition = localPos;
        line.transform.localScale = scale;
        line.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(line.GetComponent<Collider>());
    }

    private static void CreateLeg(string name, Transform parent, Vector3 localPos, Material mat)
    {
        GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leg.name = name;
        leg.transform.SetParent(parent, false);
        leg.transform.localPosition = localPos;
        leg.transform.localScale = new Vector3(0.18f, 4.0f, 0.18f);
        leg.GetComponent<Renderer>().sharedMaterial = mat;
        leg.tag = "Untagged";
    }

    private static void EnsureTags(params string[] tags)
    {
        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty tagsProp = tagManager.FindProperty("tags");

        foreach (string t in tags)
        {
            bool exists = false;
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == t)
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
            {
                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = t;
            }
        }
        tagManager.ApplyModifiedProperties();
    }
}
