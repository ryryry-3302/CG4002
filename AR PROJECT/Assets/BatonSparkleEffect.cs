using System.Collections;
using UnityEngine;
using OrchestraMaestro;

/// <summary>
/// Manages sparkle trail particle effects that follow the tracked baton tip.
/// Emission scales with baton movement speed; triggers gesture-specific bursts on judgement.
/// On successful gestures, a sparkle flies from the baton to the target orchestra member.
/// </summary>
[RequireComponent(typeof(BatonTracker))]
public class BatonSparkleEffect : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BatonTracker batonTracker;

    [Header("Particle Settings")]
    [SerializeField] private float baseEmissionRate = 30f;
    [SerializeField] private float movementEmissionScale = 80f;
    [SerializeField] private float minEmissionWhenTracking = 8f;
    [SerializeField] private float fadeOutSpeed = 4f;

    private const float FlyingSparkleDuration = 1.2f;
    private const float FlyingSparkleTrailLength = 0.4f;
    private const int ArrivalExplosionCount = 60;

    private ParticleSystem sparkleParticles;
    private ParticleSystem burstParticles;
    private ParticleSystem.EmissionModule emissionModule;
    private Vector3 lastTipPosition;
    private float currentEmissionRate;
    private float movementSpeed;
    private bool hasLastPosition;

    private void Awake()
    {
        if (batonTracker == null)
            batonTracker = GetComponent<BatonTracker>();

        EnsureParticleSystem();
    }

    private void Start()
    {
        if (RhythmGameController.Instance != null)
            RhythmGameController.Instance.OnGestureJudged += OnGestureJudged;
    }

    private void OnDestroy()
    {
        if (RhythmGameController.Instance != null)
            RhythmGameController.Instance.OnGestureJudged -= OnGestureJudged;
    }

    private void EnsureParticleSystem()
    {
        sparkleParticles = GetComponentInChildren<ParticleSystem>();
        if (sparkleParticles == null)
        {
            var go = new GameObject("BatonSparkles");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            sparkleParticles = go.AddComponent<ParticleSystem>();
            ConfigureParticleSystem();
        }
        else
        {
            ConfigureParticleSystem();
        }

        emissionModule = sparkleParticles.emission;

        // Burst effect system for gesture feedback
        var burstGo = sparkleParticles.transform.Find("Burst");
        if (burstGo == null)
        {
            burstGo = new GameObject("Burst").transform;
            burstGo.SetParent(sparkleParticles.transform);
            burstGo.localPosition = Vector3.zero;
            burstGo.localRotation = Quaternion.identity;
            burstGo.localScale = Vector3.one;
            burstParticles = burstGo.gameObject.AddComponent<ParticleSystem>();
            ConfigureBurstParticleSystem();
        }
        else
        {
            burstParticles = burstGo.GetComponent<ParticleSystem>();
        }
    }

    private void ConfigureBurstParticleSystem()
    {
        var main = burstParticles.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = 0.5f;
        main.startSpeed = 0.08f;
        main.startSize = 0.008f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;

        var emission = burstParticles.emission;
        emission.rateOverTime = 0;
        emission.enabled = true;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 50) });

        var shape = burstParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.01f;

        var colorOverLifetime = burstParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;

        var renderer = burstParticles.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var mat = GetDefaultParticleMaterial();
            if (mat != null) renderer.material = mat;
        }
    }

    private void ConfigureParticleSystem()
    {
        var main = sparkleParticles.main;
        main.duration = 1f;
        main.loop = true;
        main.startLifetime = 0.4f;
        main.startSpeed = 0.03f;
        main.startSize = 0.008f;
        main.startColor = new Color(1f, 0.95f, 0.8f, 0.9f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.15f;
        main.maxParticles = 200;

        var emission = sparkleParticles.emission;
        emission.rateOverTime = baseEmissionRate;
        emission.enabled = true;

        var shape = sparkleParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.002f;

        var colorOverLifetime = sparkleParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0.5f), new GradientColorKey(new Color(1f, 0.85f, 0.4f), 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = grad;

        var sizeOverLifetime = sparkleParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.5f, 0.8f),
            new Keyframe(1f, 0f)));

        var noise = sparkleParticles.noise;
        noise.enabled = true;
        noise.strength = 0.15f;
        noise.frequency = 1.5f;
        noise.scrollSpeed = 0.2f;

        var renderer = sparkleParticles.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var mat = GetDefaultParticleMaterial();
            if (mat != null) renderer.material = mat;
        }
    }

    private static Material GetDefaultParticleMaterial()
    {
        var shader = Shader.Find("Particles/Standard Unlit")
            ?? Shader.Find("Particles/Additive")
            ?? Shader.Find("Sprites/Default");
        if (shader == null) return null;
        var mat = new Material(shader);
        mat.SetColor("_Color", Color.white);
        mat.renderQueue = 3000;
        return mat;
    }

    private void Update()
    {
        if (batonTracker == null || sparkleParticles == null) return;

        Vector3 tipPos = batonTracker.TipWorldPosition;

        if (batonTracker.IsTracking)
        {
            transform.position = tipPos;

            if (hasLastPosition && Time.deltaTime > 0f)
                movementSpeed = Vector3.Distance(tipPos, lastTipPosition) / Time.deltaTime;
            else
                movementSpeed = 0f;
            lastTipPosition = tipPos;
            hasLastPosition = true;

            float targetRate = baseEmissionRate + movementSpeed * movementEmissionScale;
            targetRate = Mathf.Max(minEmissionWhenTracking, targetRate);
            currentEmissionRate = Mathf.Lerp(currentEmissionRate, targetRate, Time.deltaTime * 8f);
        }
        else
        {
            hasLastPosition = false;
            currentEmissionRate = Mathf.MoveTowards(currentEmissionRate, 0f, fadeOutSpeed * Time.deltaTime);
        }

        emissionModule.rateOverTime = currentEmissionRate;

        if (currentEmissionRate < 0.5f && sparkleParticles.isPlaying)
            sparkleParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        else if (currentEmissionRate >= 0.5f && !sparkleParticles.isPlaying)
            sparkleParticles.Play();
    }

    private void OnGestureJudged(ScoringResult result)
    {
        if (burstParticles == null) return;

        Vector3 pos = batonTracker != null && batonTracker.IsTracking
            ? batonTracker.TipWorldPosition
            : transform.position;

        burstParticles.transform.position = pos;

        var main = burstParticles.main;
        var emission = burstParticles.emission;
        var colorOverLifetime = burstParticles.colorOverLifetime;

        Color burstColor = Color.white;
        switch (result.judgement)
        {
            case JudgementType.Perfect:
                burstColor = new Color(1f, 0.9f, 0.4f);
                main.startColor = burstColor;
                main.startSize = 0.012f;
                main.startLifetime = 0.6f;
                main.startSpeed = 0.1f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });
                break;
            case JudgementType.Good:
                burstColor = new Color(1f, 1f, 0.95f);
                main.startColor = burstColor;
                main.startSize = 0.009f;
                main.startLifetime = 0.4f;
                main.startSpeed = 0.06f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 35) });
                break;
            case JudgementType.Miss:
                burstColor = new Color(1f, 0.4f, 0.2f);
                main.startColor = burstColor;
                main.startSize = 0.006f;
                main.startLifetime = 0.25f;
                main.startSpeed = 0.04f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 25) });
                break;
        }

        // Gesture-themed tweaks
        ApplyGestureTheme(result.gestureType, ref main, ref emission);

        var grad = new Gradient();
        Color c = burstColor;
        grad.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 0.5f), new GradientColorKey(c * 0.5f, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.4f, 0.5f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = grad;

        burstParticles.Play(true);

        // Flying sparkle to orchestra member on success
        if ((result.judgement == JudgementType.Perfect || result.judgement == JudgementType.Good)
            && OrchestraPlacement.Instance != null)
        {
            Vector3? targetPos = OrchestraPlacement.Instance.GetSectionCenterOfMass(result.targetSection);
            if (targetPos.HasValue)
                StartCoroutine(SpawnFlyingSparkle(pos, targetPos.Value, result.judgement));
        }
    }

    private IEnumerator SpawnFlyingSparkle(Vector3 from, Vector3 to, JudgementType judgement)
    {
        Vector3 dir = (to - from);
        float dist = dir.magnitude;
        if (dist < 0.01f) yield break;
        dir /= dist;

        Color sparkleColor = judgement == JudgementType.Perfect
            ? new Color(1f, 0.9f, 0.4f)
            : new Color(1f, 1f, 0.95f);

        var go = new GameObject("FlyingSparkle");
        go.transform.position = from;

        var trail = go.AddComponent<TrailRenderer>();
        trail.time = FlyingSparkleTrailLength;
        trail.startWidth = 0.04f;
        trail.endWidth = 0.002f;
        trail.material = GetDefaultParticleMaterial();
        trail.startColor = sparkleColor;
        trail.endColor = new Color(sparkleColor.r, sparkleColor.g, sparkleColor.b, 0f);
        trail.autodestruct = false;
        trail.emitting = true;

        // Glow at the head - small particle system that follows
        var headPs = go.AddComponent<ParticleSystem>();
        var main = headPs.main;
        main.duration = 0.1f;
        main.loop = true;
        main.startLifetime = 0.15f;
        main.startSize = 0.025f;
        main.startColor = sparkleColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        var headEmission = headPs.emission;
        headEmission.rateOverTime = 80f;
        var headShape = headPs.shape;
        headShape.shapeType = ParticleSystemShapeType.Sphere;
        headShape.radius = 0.005f;
        var headRenderer = headPs.GetComponent<ParticleSystemRenderer>();
        if (headRenderer != null)
        {
            headRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            headRenderer.material = GetDefaultParticleMaterial();
        }

        float elapsed = 0f;
        while (elapsed < FlyingSparkleDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / FlyingSparkleDuration;
            t = t * t * (3f - 2f * t); // smoothstep - ease in and out
            go.transform.position = Vector3.Lerp(from, to, t);
            yield return null;
        }

        go.transform.position = to;
        trail.emitting = false;

        // Explosion at arrival
        SpawnArrivalExplosion(to, sparkleColor);

        yield return new WaitForSeconds(FlyingSparkleTrailLength);
        Destroy(go);
    }

    private void SpawnArrivalExplosion(Vector3 position, Color color)
    {
        var go = new GameObject("ArrivalExplosion");
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.startLifetime = 0.5f;
        main.startSpeed = 0.15f;
        main.startSize = 0.02f;
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, ArrivalExplosionCount) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.02f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color * 0.5f, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = grad;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.5f, 1.2f), new Keyframe(1f, 0f)));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = GetDefaultParticleMaterial();
        }

        ps.Play(true);
        Destroy(go, 0.8f);
    }

    private void ApplyGestureTheme(GestureType gesture, ref ParticleSystem.MainModule main, ref ParticleSystem.EmissionModule emission)
    {
        switch (gesture)
        {
            case GestureType.PUNCH:
            case GestureType.STRONG_ACCENT:
                main.startSpeed = main.startSpeed.constant * 1.5f;
                var burst = emission.GetBurst(0);
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burst.count.constant + 20) });
                break;
            case GestureType.CIRCLE:
                main.startLifetime = main.startLifetime.constant * 1.2f;
                break;
            case GestureType.UP:
            case GestureType.V_SHAPE:
                float spd = main.startSpeed.constant;
                main.startSpeed = new ParticleSystem.MinMaxCurve(spd, spd * 1.3f);
                break;
            case GestureType.WITHDRAW:
            case GestureType.CLEAR_CUTOFF:
                main.startSize = main.startSize.constant * 0.8f;
                break;
        }
    }
}
