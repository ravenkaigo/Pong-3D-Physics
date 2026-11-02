# TECHNICAL SPECIFICATION & GDD: PONG 3D PHYSICS (IMMERSIVE EDITION)
> **Target Audience for this Document:** Autonomous AI Coding Agent / LLM Assistant & Unity Developer.
> **Engine:** Unity (2022 LTS or newer, Universal Render Pipeline / URP).
> **Target Platform:** Android Mobile (Portrait orientation, 60+ FPS).

---

## 1. SYSTEM OVERVIEW & ARCHITECTURAL GOALS

The project is an arcade physics-based 3D Pong game for Android featuring high visual fidelity and an immersive "game feel" (juiciness).
Key architectural constraints:
1. **Decoupled Physics & Visuals:** Physics collision calculations are decoupled from visual transforms to allow procedural animations (e.g., Squash & Stretch) without deforming colliders.
2. **Modular Asset Replacement:** All visual representations exist as child objects under functional parent anchors holding physics components (`Rigidbody`, `Collider`).
3. **Optimized for Mobile:** Minimize runtime allocations (`GC Alloc`), rely on transform-based or DOTween/LeanTween procedural animations, and limit draw calls.

---

## 2. IMMERSIVE VISUALS & ANIMATION SYSTEM SPECIFICATION

To achieve an immersive tactile feel, the system implements 5 interconnected visual/feedback mechanics:

### 2.1. Procedural Squash & Stretch (Ball & Paddles)
* **Principle:** When a collision occurs, deform the visual mesh along the collision normal without altering the underlying `SphereCollider` or `BoxCollider`.
* **Behavior:**
  * On impact: Visual child mesh compresses along the impact axis (e.g., scale $Z \to 0.65$) while bulging along perpendicular axes (scale $X, Y \to 1.25$) over 50-70ms.
  * Elastic rebound: Springs back past original scale ($1.1 \times$) and settles to $(1, 1, 1)$ using an elastic or bounce curve.
* **Implementation Recommendation:** Use procedural tweening (DOTween / LeanTween) or an analytical damped harmonic oscillator in a dedicated visual component (`BallVisualFX.cs`).

### 2.2. Dynamic Paddle Tilt & Lean
* **Principle:** Paddles lean dynamically based on lateral movement velocity to emulate inertia and player wrist control.
* **Behavior:**
  * When moving horizontally, rotate the visual mesh around the Z/Y-axis proportionally to current velocity ($\text{Tilt Angle} = -\text{velocity.x} \times \text{tiltMultiplier}$, clamped to $\pm 15^\circ$).
  * Smooth return to neutral alignment using `Mathf.Lerp` / `Mathf.SmoothDamp`.

### 2.3. Dynamic Camera & Screen Shake (Trauma System)
* **Cinematic Framing:** Overhead 3D perspective ($50^\circ-60^\circ$ FOV), tracking ball Z-position with a subtle clamped damping.
* **Trauma System:**
  * Light bounce: Trauma $+0.15$
  * High-speed smash / boundary bounce: Trauma $+0.35$
  * Goal scored: Trauma $+0.7$
  * Camera offset calculated per frame: $\text{Offset} = \text{Trauma}^2 \times \text{MaxShakeMultiplier} \times \text{PerlinNoise}$.

### 2.4. Trail & Particle Burst Feedback
* **Ball Trail:** Visual gradient and emission intensity scale up linearly with the ball's kinetic velocity.
* **Impact Sparks:** Emit directional particle bursts at `ContactPoint.point` oriented along `ContactPoint.normal`.
* **Smash Ring:** Expanding billboarded ring shockwave on high-velocity paddle impacts.

### 2.5. Mobile Haptics & Hit-Stop (Micro Freeze-frame)
* **Hit-Stop:** Pause time scale (`Time.timeScale = 0.05f`) for 20-40 milliseconds on critical smashes to emphasize weight.
* **Android Vibration:** Trigger `Handheld.Vibrate()` or `VibrationEffect.createOneShot()` with short durations (20ms for bounce, 60ms for goal).

---

## 3. GAME OBJECT HIERARCHY & COMPONENT BLUEPRINT

To guarantee smooth replacement of primitive placeholder assets with production 3D models:

```
[Hierarchy Root]
│
├── Camera_Rig (CameraFollow.cs, CameraShake.cs)
│   └── Main Camera (URP Post-Processing enabled)
│
├── Table_Holder (Static, TableBounds.cs)
│   ├── Colliders (Invisible walls with PhysicMaterial: Bounciness=1, Friction=0)
│   └── Visual_Mesh (Sub-mesh child for swap)
│
├── Player_Paddle (Rigidbody: IsKinematic=true, BoxCollider, PlayerPaddleController.cs)
│   └── Visual_Pivot (PaddleVisuals.cs - handles tilts and squash)
│       └── Model_PrimitiveCube (Replaceable with custom 3D mesh)
│
├── AI_Paddle (Rigidbody: IsKinematic=true, BoxCollider, AIPaddleController.cs)
│   └── Visual_Pivot (PaddleVisuals.cs)
│       └── Model_PrimitiveCube (Replaceable with custom 3D mesh)
│
├── Ball_Entity (Rigidbody: ContinuousDynamic, SphereCollider, BallController.cs)
│   ├── Visual_Pivot (BallVisualFX.cs - handles squash & stretch)
│   │   └── Model_PrimitiveSphere (Replaceable with custom 3D mesh)
│   ├── FX_TrailRenderer
│   └── FX_ImpactSparks (ParticleSystem)
│
├── Triggers
│   ├── PlayerGoalZone (BoxCollider: IsTrigger=true, GoalTrigger.cs)
│   └── AIGoalZone (BoxCollider: IsTrigger=true, GoalTrigger.cs)
│
└── Managers
    ├── GameManager.cs
    ├── AudioManager.cs
    └── HapticManager.cs
```

---

## 4. MATHEMATICAL & PHYSICS MODELS FOR SCRIPT GENERATION

### 4.1. Ball Velocity Scaling & Angle Deflection
When the ball collides with a paddle, its reflection angle depends on the point of contact relative to the center of the paddle:

$$\text{HitFactor} = \frac{Ball.X - Paddle.X}{Paddle.Width / 2}$$

$$\vec{V}_{\text{new}} = \text{Normalize}\left(\begin{bmatrix} \text{HitFactor} \times \text{DeflectionWeight} \\ 0 \\ \text{DirectionZ} \end{bmatrix}\right) \times \min(v_{\text{current}} \times 1.05, v_{\text{max}})$$

### 4.2. Camera Shake Trauma Decay
Shake trauma $T \in [0, 1]$ decays linearly over time:

$$T_{t + \Delta t} = \max(0, T_t - \text{DecayRate} \times \Delta t)$$

---

## 5. STEP-BY-STEP INSTRUCTIONS FOR AI AGENT EXECUTION

The AI agent implementing this project must produce standalone C# scripts in the following order.

### Task 1: `BallVisualFX.cs`
* **Requirements:**
  * Must be attached to `Visual_Pivot` under `Ball_Entity`.
  * Exposes public method `PlayImpactSquash(Vector3 impactNormal, float impactForce)`.
  * Smoothly scales the visual transform to compress on the impact axis and expand on the perpendicular axes, then recovers smoothly using an elastic curve.
  * Updates `TrailRenderer.startWidth` and `emissionColor` intensity according to current velocity.

### Task 2: `BallController.cs`
* **Requirements:**
  * Manages `Rigidbody` physics with `CollisionDetectionMode.ContinuousDynamic`.
  * Increases speed by $5\%$ per paddle bounce up to `maxSpeed`.
  * Calls `BallVisualFX.PlayImpactSquash()` and triggers particle sparks at `collision.contacts[0]`.
  * Exposes `ResetBall(Vector3 serveDirection)` and `LaunchBall()`.

### Task 3: `PlayerPaddleController.cs` & `PaddleVisuals.cs`
* **Requirements:**
  * Reads horizontal touch input via delta position (`Input.GetTouch(0).deltaPosition` or New Input System).
  * Moves using `Rigidbody.MovePosition()` in `FixedUpdate()` bounded by `Mathf.Clamp`.
  * Measures horizontal velocity and instructs `PaddleVisuals.cs` to tilt smoothly toward the movement direction.

### Task 4: `AIPaddleController.cs`
* **Requirements:**
  * Interpolates toward `Ball_Entity.transform.position.x` with adjustable error margin and reaction speed.
  * Drives visual tilts via `PaddleVisuals.cs`.

### Task 5: `CameraShake.cs` & `HitStopManager.cs`
* **Requirements:**
  * Implements non-linear trauma shake using Perlin noise.
  * Implements `TriggerHitStop(float durationSeconds)` using `Time.timeScale` and `yield WaitForSecondsRealtime`.

### Task 6: `GameManager.cs`
* **Requirements:**
  * Manages round states: `Serve`, `Play`, `ScoreRound`, `GameOver`.
  * Coordinates scoring triggers, UI text updates, and ball resets.

---

## 6. ASSET REPLACEMENT SPECIFICATION (MODULAR SWAP)

When replacing Unity primitives with custom 3D models (`.fbx`, `.obj`, `.gltf`):
1. **Never attach scripts or colliders to custom 3D model roots.**
2. Place the imported 3D prefab as a direct child of the corresponding `Visual_Pivot`.
3. Set the local position, rotation, and scale of the new 3D model to neutral $(0, 0, 0)$ and scale $(1, 1, 1)$.
4. Adjust the dimensions of the parent `BoxCollider` or `SphereCollider` in the Unity Inspector to match the external boundaries of the new mesh.