using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using System.Linq;

public static class PrefabInspectorSelected
{
    [MenuItem("Tools/Prefabs/Analyze Selected Prefabs")] 
    public static void AnalyzeSelectedPrefabs()
    {
        var selection = Selection.objects;
        if (selection == null || selection.Length == 0)
        {
            Debug.LogWarning("No assets selected. Please select one or more Prefabs in the Project window.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== Prefab Components Report (Selected) ===");
        sb.AppendLine($"Generated: {DateTime.Now}");
        sb.AppendLine();

        foreach (var obj in selection)
        {
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"[SKIP] {path} is not a prefab.");
                continue;
            }

            try
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null)
                {
                    sb.AppendLine($"[ERROR] Failed to load prefab: {path}");
                    continue;
                }

                sb.AppendLine($"Prefab: {path}");
                sb.AppendLine($"Root: {root.name}");

                var all = root.GetComponentsInChildren<Component>(true);
                foreach (var c in all)
                {
                    if (c == null) { sb.AppendLine("- <Missing Script>"); continue; }
                    var goPath = GetHierarchyPath(c.transform);
                    sb.AppendLine($"- {goPath} :: {SummarizeComponent(c)}");
                }

                PrefabUtility.UnloadPrefabContents(root);
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                Debug.LogError($"AnalyzeSelectedPrefabs exception on {path}: {ex}");
                sb.AppendLine($"[EXCEPTION] {path}: {ex.Message}");
            }
        }

        var reportPath = "Assets/Editor/PrefabComponentsReport.txt";
        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        Debug.Log($"Prefab Components Report written: {reportPath}");
        AssetDatabase.Refresh();
    }

    [MenuItem("Tools/Prefabs/Analyze Selected Prefabs (Extended)")]
    public static void AnalyzeSelectedPrefabsExtended()
    {
        var selection = Selection.objects;
        if (selection == null || selection.Length == 0)
        {
            Debug.LogWarning("No assets selected. Please select one or more Prefabs in the Project window.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== Prefab Components Report (Selected - Extended) ===");
        sb.AppendLine($"Generated: {DateTime.Now}");
        sb.AppendLine();

        foreach (var obj in selection)
        {
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"[SKIP] {path} is not a prefab.");
                continue;
            }

            try
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                if (root == null)
                {
                    sb.AppendLine($"[ERROR] Failed to load prefab: {path}");
                    continue;
                }

                sb.AppendLine($"Prefab: {path}");
                sb.AppendLine($"Root: {root.name}");

                var all = root.GetComponentsInChildren<Component>(true);
                foreach (var c in all)
                {
                    if (c == null) { sb.AppendLine("- <Missing Script>"); continue; }
                    SummarizeComponentExtended(c, sb);
                }

                PrefabUtility.UnloadPrefabContents(root);
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                Debug.LogError($"AnalyzeSelectedPrefabsExtended exception on {path}: {ex}");
                sb.AppendLine($"[EXCEPTION] {path}: {ex.Message}");
            }
        }

        var reportPath = "Assets/Editor/PrefabComponentsReport_Extended.txt";
        File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
        Debug.Log($"Prefab Components Extended Report written: {reportPath}");
        AssetDatabase.Refresh();
    }

    private static string GetHierarchyPath(Transform t)
    {
        var stack = new System.Collections.Generic.Stack<string>();
        while (t != null)
        {
            stack.Push(t.name);
            t = t.parent;
        }
        return string.Join("/", stack);
    }

    private static string SummarizeComponent(Component c)
    {
        var type = c.GetType();
        var typeName = type.Name;

        if (typeName == "PlayMakerFSM")
        {
            // Try get basic info: FsmName and StartState
            string fsmName = null;
            var fsmNameField = type.GetField("fsmName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (fsmNameField != null)
            {
                fsmName = fsmNameField.GetValue(c) as string;
            }
            if (string.IsNullOrEmpty(fsmName))
            {
                var fsmNameProp = type.GetProperty("FsmName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fsmNameProp != null) fsmName = fsmNameProp.GetValue(c, null) as string;
            }

            var fsmField = type.GetField("fsm", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            object fsmObj = fsmField != null ? fsmField.GetValue(c) : null;
            if (fsmObj == null)
            {
                var fsmProp = type.GetProperty("Fsm", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fsmProp != null) fsmObj = fsmProp.GetValue(c, null);
            }

            if (fsmObj != null)
            {
                var nameProp = fsmObj.GetType().GetProperty("Name", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var startStateProp = fsmObj.GetType().GetProperty("StartState", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var nameVal = nameProp != null ? nameProp.GetValue(fsmObj, null) as string : null;
                var startStateVal = startStateProp != null ? startStateProp.GetValue(fsmObj, null) as string : null;
                fsmName = string.IsNullOrEmpty(fsmName) ? nameVal : fsmName;
                if (!string.IsNullOrEmpty(fsmName) || !string.IsNullOrEmpty(startStateVal))
                {
                    return $"PlayMakerFSM(name='{fsmName}', startState='{startStateVal}')";
                }
            }
            return "PlayMakerFSM";
        }

        if (typeName == "DamageHero")
        {
            var damageDealtField = type.GetField("damageDealt") ?? type.GetField("damage");
            var hazardTypeField = type.GetField("hazardType");
            var shadowDashHazardField = type.GetField("shadowDashHazard");
            var resetOnEnableField = type.GetField("resetOnEnable");

            int damageDealt = damageDealtField != null ? Convert.ToInt32(damageDealtField.GetValue(c)) : -1;
            object hazardVal = hazardTypeField != null ? hazardTypeField.GetValue(c) : null;
            string hazardStr = hazardVal != null ? hazardVal.ToString() : "";
            bool shadowDashHazard = shadowDashHazardField != null && (bool)shadowDashHazardField.GetValue(c);
            bool resetOnEnable = resetOnEnableField != null && (bool)resetOnEnableField.GetValue(c);
            return $"DamageHero(damage={damageDealt}, hazardType={hazardStr}, shadowDashHazard={shadowDashHazard}, resetOnEnable={resetOnEnable})";
        }

        if (typeName == "AlertRange")
        {
            var col = c.GetComponent<Collider2D>();
            string colInfo = col != null ? $"Collider2D(type={col.GetType().Name}, isTrigger={col.isTrigger})" : "Collider2D(none)";
            return $"AlertRange({colInfo})";
        }

        if (typeName == "HealthManager")
        {
            var hpField = type.GetField("hp") ?? type.GetField("maxHP") ?? type.GetField("startingHP");
            var invulnField = type.GetField("invincible") ?? type.GetField("isInvincible") ?? type.GetField("invuln") ?? type.GetField("ignoreHitPause");
            string hpStr = hpField != null ? hpField.GetValue(c)?.ToString() ?? "" : "";
            string invStr = invulnField != null ? invulnField.GetValue(c)?.ToString() ?? "" : "";
            return $"HealthManager(hp={hpStr}, invuln={invStr})";
        }

        if (typeName == "Walker" || typeName == "Crawler" || typeName == "Climber")
        {
            return typeName;
        }

        if (c is Collider2D col2d)
        {
            return $"{typeName}(isTrigger={col2d.isTrigger})";
        }
        if (c is Rigidbody2D rb2d)
        {
            return $"Rigidbody2D(bodyType={rb2d.bodyType}, gravityScale={rb2d.gravityScale})";
        }
        if (c is SpriteRenderer sr)
        {
            return $"SpriteRenderer(sprite={(sr.sprite? sr.sprite.name : "<none>")}, flipX={sr.flipX})";
        }
        if (c is Animator anim)
        {
            return $"Animator(runtimeAnimatorController={(anim.runtimeAnimatorController? anim.runtimeAnimatorController.name : "<none>")})";
        }
        if (c is AudioSource aud)
        {
            return $"AudioSource(clip={(aud.clip? aud.clip.name : "<none>")}, playOnAwake={aud.playOnAwake})";
        }

        return typeName;
    }

    private static void SummarizeComponentExtended(Component c, StringBuilder sb)
    {
        var type = c.GetType();
        var typeName = type.Name;
        var goPath = GetHierarchyPath(c.transform);
        sb.AppendLine($"- {goPath} :: {SummarizeComponent(c)}");
    
        // Extra details for specific components
        if (typeName == "PlayMakerFSM")
        {
            var fsmField = type.GetField("fsm", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fsmObj = fsmField != null ? fsmField.GetValue(c) : null;
            if (fsmObj == null)
            {
                var fsmProp = type.GetProperty("Fsm", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fsmProp != null) fsmObj = fsmProp.GetValue(c, null);
            }
    
            if (fsmObj != null)
            {
                SummarizePlayMakerFSMDetailed(c, fsmObj, sb);
            }
            else
            {
                sb.AppendLine("    FSM: <unavailable via reflection>");
            }
        }
        else if (c is Collider2D col)
        {
            sb.AppendLine($"    Collider: {SummarizeColliderDetail(col)}");
        }
        else if (c is Rigidbody2D rb)
        {
            sb.AppendLine($"    Rigidbody2D: mass={rb.mass}, bodyType={rb.bodyType}, gravityScale={rb.gravityScale}, drag={rb.drag}, angularDrag={rb.angularDrag}, constraints={rb.constraints}, collisionDetectionMode={rb.collisionDetectionMode}");
        }
        else if (c is Animator anim)
        {
            sb.AppendLine($"    Animator: speed={anim.speed}, enabled={anim.enabled}, updateMode={anim.updateMode}, cullingMode={anim.cullingMode}");
        }
        else if (typeName == "tk2dSprite")
        {
            sb.AppendLine("    tk2dSprite: " + SummarizeTk2dSprite(c));
        }
        else if (typeName == "MeshRenderer")
        {
            var mr = c as MeshRenderer;
            if (mr != null)
            {
                var mats = mr.sharedMaterials?.Select(m => m ? m.name : "<null>").ToArray() ?? new string[0];
                sb.AppendLine($"    MeshRenderer: materials=[{string.Join(", ", mats)}]");
            }
        }
        else if (typeName == "DamageHero")
        {
            var hazardVal = c.GetType().GetField("hazardType")?.GetValue(c);
            var hazardStr = hazardVal != null ? hazardVal.ToString() : "";
            sb.AppendLine($"    DamageHero: hazardType={hazardStr}");
        }
        else if (typeName == "LineOfSightDetector")
        {
            var losFields = new[] { "maxDistance", "viewRadius", "viewAngle", "layerMask", "targetLayer", "target", "obstacleMask", "raycastSpacing" };
            var parts = new System.Collections.Generic.List<string>();
            foreach (var f in losFields)
            {
                var got = TryGetFieldValue(c, f);
                if (got.exists) parts.Add($"{f}={FormatValue(got.value)}");
            }
            if (parts.Count > 0) sb.AppendLine("    LineOfSight: " + string.Join(", ", parts));
        }
        else if (typeName == "Walker" || typeName == "Crawler" || typeName == "Climber")
        {
            var speed = TryGetFieldValue(c, "speed").value;
            var accel = TryGetFieldValue(c, "acceleration").value;
            var moveDuringAttack = TryGetFieldValue(c, "moveDuringAttack").value;
            var maxSpeed = TryGetFieldValue(c, "maxSpeed").value;
            var dir = TryGetFieldValue(c, "direction").value;
            var parts = new System.Collections.Generic.List<string>();
            if (speed != null) parts.Add($"speed={FormatValue(speed)}");
            if (accel != null) parts.Add($"accel={FormatValue(accel)}");
            if (maxSpeed != null) parts.Add($"maxSpeed={FormatValue(maxSpeed)}");
            if (moveDuringAttack != null) parts.Add($"moveDuringAttack={FormatValue(moveDuringAttack)}");
            if (dir != null) parts.Add($"direction={FormatValue(dir)}");
            if (parts.Count > 0) sb.AppendLine($"    {typeName}: " + string.Join(", ", parts));
        }
        else if (typeName == "HealthManager")
        {
            var hp = TryGetFieldValue(c, "hp").value ?? TryGetFieldValue(c, "maxHP").value ?? TryGetFieldValue(c, "startingHP").value;
            var inv = TryGetFieldValue(c, "invincible").value ?? TryGetFieldValue(c, "isInvincible").value ?? TryGetFieldValue(c, "invuln").value;
            sb.AppendLine($"    HealthManager: hp={FormatValue(hp)}, invincible={FormatValue(inv)}");
        }
    }

    private static string SummarizeColliderDetail(Collider2D col)
    {
        try
        {
            if (col is BoxCollider2D bc)
            {
                return $"Box(size={bc.size}, offset={bc.offset}, isTrigger={bc.isTrigger})";
            }
            if (col is CircleCollider2D cc)
            {
                return $"Circle(radius={cc.radius}, offset={cc.offset}, isTrigger={cc.isTrigger})";
            }
            if (col is CapsuleCollider2D cap)
            {
                return $"Capsule(size={cap.size}, direction={cap.direction}, offset={cap.offset}, isTrigger={cap.isTrigger})";
            }
            if (col is PolygonCollider2D pc)
            {
                return $"Polygon(paths={pc.pathCount}, totalPoints={pc.GetTotalPointCount()}, isTrigger={pc.isTrigger})";
            }
        }
        catch { }
        return $"{col.GetType().Name}(isTrigger={col.isTrigger})";
    }

    private static string SummarizeTk2dSprite(Component c)
    {
        try
        {
            var type = c.GetType();
            var spriteIdField = type.GetField("spriteId");
            var collectionField = type.GetField("collection");
            var colorProp = type.GetProperty("color");
            var spriteId = spriteIdField != null ? spriteIdField.GetValue(c)?.ToString() : null;
            string collectionName = null;
            if (collectionField != null)
            {
                var collObj = collectionField.GetValue(c);
                collectionName = TryGetPropertyString(collObj, "name") ?? TryGetFieldString(collObj, "name");
            }
            var colorStr = colorProp != null ? colorProp.GetValue(c, null)?.ToString() : null;
            return $"spriteId={spriteId}, collection={(collectionName ?? "<none>")}, color={colorStr}";
        }
        catch
        {
            return "<tk2d details unavailable>";
        }
    }

    private static (bool exists, object value) TryGetFieldValue(object obj, string fieldName)
    {
        if (obj == null) return (false, null);
        var f = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null) return (true, f.GetValue(obj));
        var p = obj.GetType().GetProperty(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (p != null) return (true, p.GetValue(obj, null));
        return (false, null);
    }

    private static object TryGetProperty(object obj, string prop)
    {
        if (obj == null) return null;
        var p = obj.GetType().GetProperty(prop, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return p != null ? p.GetValue(obj, null) : null;
    }
    private static string TryGetPropertyString(object obj, string prop)
    {
        var v = TryGetProperty(obj, prop);
        return v != null ? v.ToString() : null;
    }
    private static string TryGetFieldString(object obj, string field)
    {
        var f = obj?.GetType().GetField(field, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return f != null ? f.GetValue(obj)?.ToString() : null;
    }
    private static string FormatValue(object v)
    {
        if (v == null) return "";
        if (v is UnityEngine.Object uo) return uo ? uo.name : "<null>";
        if (v is LayerMask lm) return lm.value.ToString();
        return v.ToString();
    }
    // Append indent using two spaces per level
    private static void AppendIndent(StringBuilder sb, int indentLevels)
    {
        for (int i = 0; i < indentLevels; i++) sb.Append("  ");
    }

    // Format action parameter value, with special handling for PlayMaker Fsm types
    private static string FormatActionParamValue(object v)
    {
        if (v == null) return "<null>";
        var t = v.GetType();
        if (t.IsArray)
        {
            var a = v as Array;
            int len = a?.Length ?? 0;
            int sampleCount = Math.Min(len, 5);
            var samples = new System.Collections.Generic.List<string>();
            for (int i = 0; i < sampleCount; i++)
            {
                try { samples.Add(FormatValue(a.GetValue(i))); } catch { samples.Add("<err>"); }
            }
            if (len == 0) return "Array(len=0)";
            return $"Array(len={len}, sample=[{string.Join(", ", samples)}]{(len > sampleCount ? ", ..." : "")})";
        }

        var typeName = t.FullName ?? t.Name;
        bool isFsmType = typeName != null && (typeName.Contains("HutongGames.PlayMaker") && typeName.Contains("Fsm") || typeName.StartsWith("Fsm"));
        if (isFsmType)
        {
            try
            {
                var p = t.GetProperty("Value", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (p != null)
                {
                    var pv = p.GetValue(v, null);
                    return FormatValue(pv);
                }
                var f = t.GetField("Value", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null)
                {
                    var fv = f.GetValue(v);
                    return FormatValue(fv);
                }
                var goProp = t.GetProperty("GameObject", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (goProp != null)
                {
                    var gov = goProp.GetValue(v, null);
                    return FormatValue(gov);
                }
            }
            catch { }
        }

        return FormatValue(v);
    }

    // Summarize parameters of a PlayMaker action by listing declared fields and properties
    private static void SummarizeActionParameters(object action, StringBuilder sb, int indentLevels)
    {
        try
        {
            var t = action.GetType();
            var fields = t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
            fields = fields.OrderBy(f => f.Name).ToArray();
            foreach (var f in fields)
            {
                object val = null;
                try { val = f.GetValue(action); } catch { val = "<error>"; }
                AppendIndent(sb, indentLevels);
                sb.AppendLine($"{f.Name} = {FormatActionParamValue(val)}");
            }

            var props = t.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
            props = props.OrderBy(p => p.Name).ToArray();
            foreach (var p in props)
            {
                try
                {
                    var getter = p.GetGetMethod(true);
                    if (getter == null || getter.GetParameters().Length != 0) continue;
                    object val = p.GetValue(action, null);
                    AppendIndent(sb, indentLevels);
                    sb.AppendLine($"{p.Name} = {FormatActionParamValue(val)}");
                }
                catch { }
            }
        }
        catch { }
    }
    private static void SummarizePlayMakerFSMDetailed(Component c, object fsmObj, StringBuilder sb)
    {
        try
        {
            string name = TryGetPropertyString(fsmObj, "Name");
            string start = TryGetPropertyString(fsmObj, "StartState");

            var statesObj = TryGetProperty(fsmObj, "States");
            Array arrStates = statesObj as Array;
            int stateCount = arrStates != null ? arrStates.Length : -1;

            var eventsObj = TryGetProperty(fsmObj, "Events");
            Array arrEvents = eventsObj as Array;
            int eventCount = arrEvents != null ? arrEvents.Length : -1;

            string varsSummary = null;
            var varsObj = TryGetProperty(fsmObj, "Variables");
            if (varsObj != null)
            {
                try
                {
                    var fields = varsObj.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var kv = new System.Collections.Generic.List<string>();
                    foreach (var f in fields)
                    {
                        var v = f.GetValue(varsObj);
                        if (v is Array a)
                        {
                            kv.Add($"{f.Name}={a.Length}");
                        }
                    }
                    if (kv.Count > 0) varsSummary = string.Join(", ", kv);
                }
                catch { }
            }

            sb.AppendLine($"    FSM: name='{name}', start='{start}', states={stateCount}, events={eventCount}");

            var stateNames = new System.Collections.Generic.List<string>();
            if (arrStates != null)
            {
                for (int i = 0; i < arrStates.Length; i++)
                {
                    var s = arrStates.GetValue(i);
                    var sn = TryGetPropertyString(s, "Name");
                    if (!string.IsNullOrEmpty(sn)) stateNames.Add(sn);
                }
            }
            if (stateNames.Count > 0) sb.AppendLine($"    States: {string.Join(", ", stateNames)}");

            var eventNames = new System.Collections.Generic.List<string>();
            if (arrEvents != null)
            {
                for (int i = 0; i < arrEvents.Length; i++)
                {
                    var e = arrEvents.GetValue(i);
                    var en = TryGetPropertyString(e, "Name");
                    if (!string.IsNullOrEmpty(en)) eventNames.Add(en);
                }
            }
            if (eventNames.Count > 0) sb.AppendLine($"    Events: {string.Join(", ", eventNames)}");

            if (!string.IsNullOrEmpty(varsSummary))
                sb.AppendLine($"    Vars: {varsSummary}");

            if (arrStates != null)
            {
                for (int i = 0; i < arrStates.Length; i++)
                {
                    var s = arrStates.GetValue(i);
                    var sn = TryGetPropertyString(s, "Name");

                    var actionsObj = TryGetProperty(s, "Actions");
                    Array arrActions = actionsObj as Array;
                    var transitionsObj = TryGetProperty(s, "Transitions");
                    Array arrTrans = transitionsObj as Array;

                    sb.AppendLine($"    State[{i + 1}]: '{sn}' actions={(arrActions?.Length ?? 0)}, transitions={(arrTrans?.Length ?? 0)}");

                    if (arrActions != null)
                    {
                        for (int j = 0; j < arrActions.Length; j++)
                        {
                            var act = arrActions.GetValue(j);
                            var tName = act != null ? act.GetType().Name : "<null>";
                            sb.AppendLine($"      Action[{j + 1}]: {tName}");
                            if (act != null)
                            {
                                SummarizeActionParameters(act, sb, 4);
                            }
                        }
                    }

                    if (arrTrans != null && arrTrans.Length > 0)
                    {
                        for (int j = 0; j < arrTrans.Length; j++)
                        {
                            var tr = arrTrans.GetValue(j);
                            string evName = TryGetPropertyString(tr, "EventName");
                            if (string.IsNullOrEmpty(evName))
                            {
                                var evObj = TryGetProperty(tr, "Event");
                                evName = TryGetPropertyString(evObj, "Name");
                            }
                            string toState = TryGetPropertyString(tr, "ToState");
                            if (string.IsNullOrEmpty(toState))
                            {
                                toState = TryGetPropertyString(tr, "ToStateName");
                            }
                            sb.AppendLine($"      Transition[{j + 1}]: {evName}->{toState}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"    FSM: <error reading details: {ex.Message}>");
        }
    }
}