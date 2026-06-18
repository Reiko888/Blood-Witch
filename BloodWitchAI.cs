using Dusk;
using GameNetcodeStuff;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Unity.Netcode;
using Unity.Services.Authentication.Generated;
using UnityEngine;

namespace BloodWitch
{
    [System.Serializable]
    public struct LevelMaterialSwap
    {
        public SkinnedMeshRenderer targetRenderer;
        public int materialIndex;
        public Material level1Material;
        public Material level2Material;
        public Material level3Material;
        public Material level4Material;
    }

    public class BloodWitchAI : EnemyAI
    {
        [Header("Material Swaps")]
        public LevelMaterialSwap[] grannyMaterialSwaps;

        System.Random enemyRandom = null!;

        public GameObject[] outsideNodes;

        public SkinnedMeshRenderer[] skin;

        public Material[] thisMaterial;

        private bool hasLOS;

        private float LOStimer = 0f;

        public float chaseTimer = 0f;

        public bool canTeleport = true;

        public bool hasTeleported;

        private float chosenBloodTimer;

        private float teleportCooldownTimer;

        //public float teleportCooldown = 15f;

        private Vector3 lastPosition;

        public Vector3 recentBlood;

        private Vector3 chosenTeleportNode;

        [System.Serializable]
        public class LimbGroup
        {
            public SkinnedMeshRenderer[] renderers;
        }

        public LimbGroup[] removableLimbs;

        public GameObject[] severedLimbPrefabs;

        private float[] limbRegenTimers;

        private float limbRegenDuration;

        private bool[] isLimbDetached;

        private bool isConsumingBlood = false;

        private float consumeTimer = 0f;
        private Vector3 consumptionLockPosition;

        private float consumeDuration;

        public Transform currentBloodTarget;

        private List<Transform> consumedBloodTargets = new List<Transform>();

        private float bloodSearchTimer = 0f;
        private float bloodTargetWaitTimer = 0f;
        private float currentMonsterSpeed = 2f;
        
        private Vector3 previousPosition;
        private float averageVelocity;
        private float velocityInterval;
        private float velocityAverageCount;

        public GameObject geyserPrefab;
        private GameObject currentActiveGeyser;

        public GameObject playerBloodExplosionPrefab;

        [Header("Footprints")]
        public GameObject footprintPrefab;
        public int maxFootprints = 15;
        public float footprintDistanceThreshold = 1.5f;
        public float footprintLateralOffset = 0.3f;
        public float footprintBackwardOffset = 1.2f;
        public float footprintLifetime = 4f;
        
        private float accumulatedFootprintDistance = 0f;
        private Vector3 previousFootprintPosition;
        private bool alternateFootprint = false;
        private float activeFootprintTimer = 0f;
        private GameObject[] footprintPool;
        private float[] footprintTimers;
        private int currentFootprintIndex = 0;

        public GameObject bloodOrb;
        public GameObject dagger;

        public PlayAudioAnimationEvent grannyAudioAnimationEvent;
        public PlayAudioAnimationEvent monsterAudioAnimationEvent;

        [Header("Audio")]
        public AudioClip[] hitSFX;
        public AudioClip[] monsterAttackSFX;
        public AudioClip[] monsterSeePlayerSFX;
        public AudioClip[] monsterHitPlayerSFX;
        private float monsterSeePlayerCooldown = 0f;
        public AudioClip[] laughingClips;
        public AudioClip[] level3LaughingClips;
        public AudioClip[] levelUpScreams;
        public AudioClip[] levelUpScreamsDistant;
        private float laughCooldown = 0f;
        public AudioClip bloodExplosionSFX;
        public AudioClip boilTarget2DSFX;
        public AudioClip teleportSFX;
        public AudioClip consumeBloodSFX;
        public AudioClip transformationSFX;
        public AudioClip transformationSFXDistant;
        public AudioClip stabSFX;
        public AudioSource daggerAudioSource;
        public AudioSource breathingAudioSource;
        public AudioSource screamAudioSource;
        public AudioSource distantScreamAudioSource;
        private AudioSource boil2DAudioSource;

        public GameObject grannyModelContainer;

        public GameObject monsterModelContainer;

        public GameObject portalPrefab;
        public AudioClip portalStartSFX;
        public AudioClip portalCloseSFX;
        public GameObject[] level2EyeEffects;

        public ParticleSystem BloodSpurtParticleArmL;
        public ParticleSystem BloodSpurtParticleArmR;
        public ParticleSystem BloodSpurtParticleHead;
        public ParticleSystem BloodSpurtParticleBackL;
        public ParticleSystem BloodSpurtParticleBackR;

        private float timeSinceLastAttack;
        private bool isShipLeavingHandled = false;

        public int bloodConsumed = 0;
        
        public int CurrentLevel 
        {
            get 
            {
                if (bloodConsumed >= Level3ConsumeReq) return 4;
                if (bloodConsumed >= Level2ConsumeReq) return 3;
                if (bloodConsumed >= Level1ConsumeReq) return 2;
                return 1;
            }
        }

        private float explosionTimer = 0f;
        private float lastBoilTime = 0f;
        private float explosionThreshold = 5f;
        private float geyserCooldown;
        private float geyserCooldownThreshold;
        private float grannyAttackCooldownThreshold;
        private bool hasStartedTransformation = false;
        private bool isCurrentlyTransforming = false;
        private bool isWaitingForLevelUp = false;
        private bool isPausedAfterAttack = false;
        private bool isWalkingIntoPortal = false;
        
        private EntranceTeleport[] allTeleports;
        private float doorTransitionCooldown = 0f;

        private static FieldInfo currentBloodIndexField = typeof(PlayerControllerB).GetField("currentBloodIndex", BindingFlags.NonPublic | BindingFlags.Instance);

        //Config variables
        private int Level1TPCooldown;
        private int Level2TPCooldown;
        private int Level3TPCooldown;
        private int Level1ConsumeReq;
        private int Level2ConsumeReq;
        private int Level3ConsumeReq;
        private bool canUseEnemyBlood;
        private bool canUsePlayerBlood;
        private bool canMonstGoOutside;
        private bool canGrannyGoOutside;
        private int granny1to2base;
        private int granny3base;
        private int monsterBaseSpeed;
        private int monsterMaxSpeed;
        private int limbHealth;
        private int currentLimbDamage = 0;



        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            if (Plugin.Logger != null)
            {
                Plugin.Logger.LogInfo(text);
            }
            else
            {
                UnityEngine.Debug.Log($" {text}");
            }
        }

        private void SpawnFootprint()
        {
            if (footprintPool == null || footprintPool.Length == 0) return;

            GameObject fp = footprintPool[currentFootprintIndex];
            if (fp == null) return;

            Vector3 direction = transform.forward;
            if (direction == Vector3.zero) direction = Vector3.forward;

            Vector3 rightDir = transform.right;
            float offset = alternateFootprint ? footprintLateralOffset : -footprintLateralOffset;

            Vector3 spawnPos = transform.position - (direction * footprintBackwardOffset) + (rightDir * offset) + (Vector3.up * 1.0f);

            int mask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;
            mask &= ~(1 << gameObject.layer);

            RaycastHit hit;
            if (Physics.Raycast(spawnPos, Vector3.down, out hit, 4f, mask, QueryTriggerInteraction.Ignore))
            {
                if (Vector3.Dot(hit.normal, Vector3.up) < 0.7f) return;

                fp.transform.position = hit.point + (Vector3.up * 0.02f); 

                Vector3 projectedForward = Vector3.ProjectOnPlane(direction, hit.normal).normalized;
                if (projectedForward != Vector3.zero)
                {
                    fp.transform.rotation = Quaternion.LookRotation(-hit.normal, projectedForward);
                }

                Vector3 scale = fp.transform.localScale;
                scale.x = alternateFootprint ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
                fp.transform.localScale = scale;

                fp.SetActive(true);
                footprintTimers[currentFootprintIndex] = footprintLifetime;

                alternateFootprint = !alternateFootprint;
                currentFootprintIndex = (currentFootprintIndex + 1) % maxFootprints;
            }
        }

        private void ApplyLevelMaterials(int level)
        {
            if (grannyMaterialSwaps == null || grannyMaterialSwaps.Length == 0) return;
            
            foreach (var swap in grannyMaterialSwaps)
            {
                if (swap.targetRenderer == null) continue;
                
                Material targetMat = null;
                if (level == 1 && swap.level1Material != null) targetMat = swap.level1Material;
                else if (level == 2 && swap.level2Material != null) targetMat = swap.level2Material;
                else if (level == 3 && swap.level3Material != null) targetMat = swap.level3Material;
                else if (level >= 4 && swap.level4Material != null) targetMat = swap.level4Material;
                
                if (targetMat != null)
                {
                    Material[] mats = swap.targetRenderer.sharedMaterials;
                    if (swap.materialIndex >= 0 && swap.materialIndex < mats.Length)
                    {
                        mats[swap.materialIndex] = targetMat;
                        swap.targetRenderer.sharedMaterials = mats;
                    }
                }
            }

            if (level2EyeEffects != null)
            {
                foreach (var eye in level2EyeEffects)
                {
                    if (eye != null) eye.SetActive(level >= 2);
                }
            }
        }

        public override void Start()
        {
            base.Start();
            Level1TPCooldown= BWContentHandler.Instance.bwAssets.GetConfig<int>("Level 1: Teleport cooldown").Value;
            consumeDuration = BWContentHandler.Instance.bwAssets.GetConfig<int>("Levels 1-3: Consume blood duration").Value;
            grannyAttackCooldownThreshold = BWContentHandler.Instance.bwAssets.GetConfig<float>("Levels 1-3: Granny stab attack cooldown").Value;
            explosionThreshold = BWContentHandler.Instance.bwAssets.GetConfig<float>("Level 3: Player explosion timer").Value;
            geyserCooldownThreshold = BWContentHandler.Instance.bwAssets.GetConfig<float>("Levels 2: Geyser ability cooldown").Value;
            Level2TPCooldown = BWContentHandler.Instance.bwAssets.GetConfig<int>("Level 2: Teleport cooldown").Value;
            Level3TPCooldown = BWContentHandler.Instance.bwAssets.GetConfig<int>("Level 3: Teleport cooldown").Value;
            Level1ConsumeReq = BWContentHandler.Instance.bwAssets.GetConfig<int>("Level 1: Blood Consumption level up requirement").Value;
            Level2ConsumeReq = BWContentHandler.Instance.bwAssets.GetConfig<int>("Level 2: Blood Consumption level up requirement").Value;
            Level3ConsumeReq = BWContentHandler.Instance.bwAssets.GetConfig<int>("Level 3: Blood Consumption level up requirement").Value;
            canUseEnemyBlood = BWContentHandler.Instance.bwAssets.GetConfig<bool>("Levels 1-3: Level up on enemy blood").Value;
            canUsePlayerBlood = BWContentHandler.Instance.bwAssets.GetConfig<bool>("Levels 1-3: Level up on player damage blood").Value;
            canMonstGoOutside = BWContentHandler.Instance.bwAssets.GetConfig<bool>("Level 4: Can monster go outside?").Value;
            canGrannyGoOutside = BWContentHandler.Instance.bwAssets.GetConfig<bool>("Levels 1-3: Consume outside blood").Value;
            granny1to2base = BWContentHandler.Instance.bwAssets.GetConfig<int>("Levels 1-2: Granny base speed").Value;
            granny3base = BWContentHandler.Instance.bwAssets.GetConfig<int>("Level 3: Speed of granny").Value;
            monsterBaseSpeed = BWContentHandler.Instance.bwAssets.GetConfig<int>("Level 4: Base monster speed").Value;
            monsterMaxSpeed = BWContentHandler.Instance.bwAssets.GetConfig<int>("Level 4: Max monster speed").Value;
            limbHealth = BWContentHandler.Instance.bwAssets.GetConfig<int>("Levels 1-3: Limb health").Value;
            limbRegenDuration = BWContentHandler.Instance.bwAssets.GetConfig<float>("Levels 1-3: Limb regeneration duration").Value;

            updatePositionThreshold = 0.5f;
            ApplyLevelMaterials(CurrentLevel);
            
            if (footprintPrefab != null)
            {
                footprintPool = new GameObject[maxFootprints];
                footprintTimers = new float[maxFootprints];
                for (int i = 0; i < maxFootprints; i++)
                {
                    GameObject fp = Instantiate(footprintPrefab, Vector3.zero, Quaternion.identity);
                    fp.transform.SetParent(null, true);
                    fp.SetActive(false);
                    footprintPool[i] = fp;
                    footprintTimers[i] = 0f;
                }
            }
            
            bloodSearchTimer = 2f;
            currentBehaviourStateIndex = 1;

            boil2DAudioSource = gameObject.AddComponent<AudioSource>();
            boil2DAudioSource.clip = boilTarget2DSFX;
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + this.thisEnemyIndex);
            List<Material> combinedMaterials = new List<Material>();

            if (skin != null)
            {
                foreach (SkinnedMeshRenderer s in skin)
                {
                    if (s != null)
                    {
                        combinedMaterials.AddRange(s.materials);
                    }
                }
            }
            if (!RoundManager.Instance.hasInitializedLevelRandomSeed)
            {
                RoundManager.Instance.InitializeRandomNumberGenerators();
            }
            base.GetAINodes();
            outsideNodes = GameObject.FindGameObjectsWithTag("OutsideAINode");

            if (removableLimbs != null)
            {
                limbRegenTimers = new float[removableLimbs.Length];
                isLimbDetached = new bool[removableLimbs.Length];
            }
        }

        private enum State
        {
            GrannyLevel1,
            GrannyLevel2,
            GrannyLevel3,
            MonsterLevel4
        }

        public void SyncStateSafely(int newStateIndex)
        {
            if (IsServer)
            {
                SyncStateSafelyClientRpc(newStateIndex);
            }
            else
            {
                SyncStateSafelyServerRpc(newStateIndex);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SyncStateSafelyServerRpc(int newStateIndex)
        {
            SyncStateSafelyClientRpc(newStateIndex);
        }

        [ClientRpc]
        private void SyncStateSafelyClientRpc(int newStateIndex)
        {
            LogIfDebugBuild($"State changed from {(State)currentBehaviourStateIndex} to {(State)newStateIndex}");
            currentBehaviourStateIndex = newStateIndex;
        }

        public override void DoAIInterval()
        {
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead || isCurrentlyTransforming || isPausedAfterAttack || stunNormalizedTimer > 0f || isWalkingIntoPortal)
                return;
                
            if (!isConsumingBlood)
            {
                base.DoAIInterval();
            }

            bool oldLOS = hasLOS;
            int level = CurrentLevel;
            int desiredState = level - 1;

            if (currentBehaviourStateIndex != desiredState)
            {
                LogIfDebugBuild($"Switching state from {currentBehaviourStateIndex} to {desiredState} (Level {level})");
                SyncStateSafely(desiredState);
                
                if (level == 4 && !hasStartedTransformation)
                {
                    hasStartedTransformation = true;
                    canTeleport = false; // Monster mode loses granny abilities
                    TriggerTransformationClientRpc();
                }
                else if (level < 4)
                {
                    if (grannyModelContainer != null) grannyModelContainer.SetActive(true);
                    if (monsterModelContainer != null) monsterModelContainer.SetActive(false);
                }
            }

            if (isConsumingBlood) return;

            if (IsServer && agent != null)
            {
                switch ((State)currentBehaviourStateIndex)
                {
                    case State.GrannyLevel1:
                    case State.GrannyLevel2:
                        agent.speed = granny1to2base;
                        break;
                    case State.GrannyLevel3:
                        agent.speed = granny3base;
                        break;
                    case State.MonsterLevel4:
                        // Handled in Update method where currentMonsterSpeed is clamped and applied
                        break;
                }
            }

            if (targetPlayer != null && !isConsumingBlood)
            {
                if (level == 4 && targetPlayer.isInsideFactory == this.isOutside)
                {
                    //reused and adapted from observerAI
                    movingTowardsTargetPlayer = false;
                    EntranceTeleport chaserDoor = GetClosestDoorToMonster();
                    if (chaserDoor != null)
                    {
                        SetDestinationToPosition(chaserDoor.transform.position, false);
                        if (Vector3.Distance(transform.position, chaserDoor.transform.position) < 4f && doorTransitionCooldown <= 0f)
                        {
                            EntranceTeleport exitDoor = GetCorrespondingDoor(chaserDoor);
                            if (exitDoor != null)
                            {
                                doorTransitionCooldown = 3f;
                                TeleportEnemyServerRpc(exitDoor.entrancePoint.position, !this.isOutside);
                            }
                        }
                    }
                    return;
                }

                if (CheckLineOfSightForPosition(targetPlayer.gameplayCamera.transform.position, 120f, 60))
                {
                    hasLOS = true;
                    lastPosition = targetPlayer.transform.position;
                    if (currentSearch != null && currentSearch.inProgress) StopSearch(currentSearch);
                    SetMovingTowardsTargetPlayer(targetPlayer);

                    bool leftArmMissing = (removableLimbs != null && isLimbDetached != null && isLimbDetached.Length > 0 && isLimbDetached[0]);

                    if (level == 3 && !leftArmMissing) 
                    {
                        if (explosionTimer <= 0f)
                        {
                            if (creatureAnimator != null)
                            {
                                LogIfDebugBuild($"Started boiling player {targetPlayer.playerUsername}");
                                SyncBoilingAnimationClientRpc(true, false);
                            }
                            lastBoilTime = Time.time;
                        }

                        explosionTimer += (Time.time - lastBoilTime);
                        lastBoilTime = Time.time;

                        SyncBoilTargetClientRpc((int)targetPlayer.playerClientId, explosionTimer);

                        if (explosionTimer >= explosionThreshold) 
                        {
                            LogIfDebugBuild($"Started boiling player {targetPlayer.playerUsername}");
                            SyncBoilingAnimationClientRpc(true, false);
                            targetPlayer.isSinking = false;
                        }

                        if (explosionTimer >= explosionThreshold)
                        {
                            LogIfDebugBuild($"Exploding player {targetPlayer.playerUsername}");
                            ExplodePlayer(targetPlayer);
                            targetPlayer = null;
                            explosionTimer = 0f;
                            
                            SyncBoilingAnimationClientRpc(false, true);
                            SyncBoilTargetClientRpc(-1, 0f);
                        }
                    }
                    else if (level == 2 && !leftArmMissing)
                    {
                        if (Vector3.Distance(transform.position, targetPlayer.transform.position) < 15f && geyserCooldown <= 0f)
                        {
                            LogIfDebugBuild($"Casting Geyser on player {targetPlayer.playerUsername}");
                            CastBloodGeyser(targetPlayer);
                            geyserCooldown = geyserCooldownThreshold;
                        }
                    }

                    if (leftArmMissing && explosionTimer > 0f)
                    {
                        explosionTimer = 0f;
                        SyncBoilingAnimationClientRpc(false, false);
                        SyncBoilTargetClientRpc(-1, 0f);
                    }
                }
                else
                {
                    if (hasLOS) LogIfDebugBuild("Lost Line of Sight to target player.");
                    hasLOS = false;
                    if (explosionTimer > 0f)
                    {
                        explosionTimer = 0f;
                        SyncBoilingAnimationClientRpc(false, false);
                        SyncBoilTargetClientRpc(-1, 0f);
                    }
                    explosionTimer = 0f; // Reset explosion build-up if LOS broken
                    if (chaseTimer <= 0f)
                    {
                        targetPlayer = null;
                        if (currentSearch == null || !currentSearch.inProgress) StartSearch(base.transform.position);
                    }
                    else
                    {
                        SetDestinationToPosition(lastPosition);

                        if (Vector3.Distance(transform.position, lastPosition) <= 2f)
                        {
                            chaseTimer = 0f;
                        }
                    }
                }
            }
            else
            {
                hasLOS = false;
                explosionTimer = 0f;

                if (TargetClosestPlayer(5f, requireLineOfSight: true, 120f))
                {
                    hasLOS = true;
                    chaseTimer = 10f;
                    lastPosition = targetPlayer.transform.position;

                    if (currentSearch != null && currentSearch.inProgress) StopSearch(currentSearch);
                    SetMovingTowardsTargetPlayer(targetPlayer);
                }
                else if (currentSearch == null || !currentSearch.inProgress)
                {
                    StartSearch(base.transform.position);
                }

                if (level == 4 && monsterSeePlayerCooldown <= 0f)
                {
                    // 1% chance every AI tick (approx every 0.2s = 5% per second) to play the spotted SFX while wandering
                    if (UnityEngine.Random.Range(0, 100) < 1)
                    {
                        monsterSeePlayerCooldown = 15f; // Wait at least 15 seconds before playing again
                        if (IsServer) PlayMonsterSeePlayerSFXClientRpc();
                    }
                }

                if (level == 4 && doorTransitionCooldown <= 0f)
                {
                    EntranceTeleport chaserDoor = GetClosestDoorToMonster();
                    //if monster walks within 4 units of entrance
                    if (chaserDoor != null && Vector3.Distance(transform.position, chaserDoor.transform.position) < 4f)
                    {
                        // 30% chance to go through the door when wandering near it to avoid constant ping-ponging
                        if (UnityEngine.Random.Range(0, 100) < 30)
                        {
                            EntranceTeleport exitDoor = GetCorrespondingDoor(chaserDoor);
                            if (exitDoor != null)
                            {
                                doorTransitionCooldown = 15f;
                                TeleportEnemyServerRpc(exitDoor.entrancePoint.position, !this.isOutside);
                            }
                        }
                        else
                        {
                            doorTransitionCooldown = 5f; // Check again in 5 seconds
                        }
                    }
                }
            }

            if (!oldLOS && hasLOS && CurrentLevel == 4 && monsterSeePlayerCooldown <= 0f)
            {
                monsterSeePlayerCooldown = 5f;
                if (IsServer) PlayMonsterSeePlayerSFXClientRpc();
            }
        }

        private Vector3 targetPlayerLastPos;
        private Vector3 targetPlayerVelocity;

        public override void Update()
        {
            base.Update();
            
            if (StartOfRound.Instance != null && StartOfRound.Instance.shipIsLeaving && !isShipLeavingHandled)
            {
                isShipLeavingHandled = true;
                HandleShipLeft();
            }

            if (isEnemyDead) return;

            if (stunNormalizedTimer > 0f)
            {
                if (creatureAnimator != null && !creatureAnimator.GetBool("hasBeenStunned"))
                {
                    creatureAnimator.SetBool("hasBeenStunned", true);
                }
                if (IsServer && agent != null && agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }
                return;
            }
            else
            {
                if (creatureAnimator != null && creatureAnimator.GetBool("hasBeenStunned"))
                {
                    creatureAnimator.SetBool("hasBeenStunned", false);
                    if (IsServer && agent != null && agent.isOnNavMesh)
                    {
                        agent.isStopped = false;
                    }
                }
            }

            if (isWalkingIntoPortal) return;

            if (doorTransitionCooldown > 0f) doorTransitionCooldown -= Time.deltaTime;
            if (monsterSeePlayerCooldown > 0f) monsterSeePlayerCooldown -= Time.deltaTime;

            if (targetPlayer != null)
            {
                targetPlayerVelocity = (targetPlayer.transform.position - targetPlayerLastPos) / Time.deltaTime;
                targetPlayerLastPos = targetPlayer.transform.position;
            }

            if (hasTeleported)
            {
                LogIfDebugBuild($"Processing hasTeleported and starting consumption.");
                hasTeleported = false;

                if (consumeCoroutine != null) StopCoroutine(consumeCoroutine);
                consumeCoroutine = StartCoroutine(TeleportThenConsumeRoutine());
            }

            if (currentBehaviourStateIndex == 3) //This is monster level, which works as 0,1,2,3 instead of level int 1,2,3,4
            {
                if (IsServer && agent != null && agent.isOnNavMesh && !isPausedAfterAttack && !isCurrentlyTransforming)
                {
                    if (targetPlayer != null && hasLOS)
                    {
                        currentMonsterSpeed += Time.deltaTime * 2.5f;
                        agent.acceleration = Mathf.MoveTowards(agent.acceleration, 35f, Time.deltaTime * 5f);
                    }
                    else
                    {
                        currentMonsterSpeed -= Time.deltaTime * 4f; // Slow down if no target
                        agent.acceleration = Mathf.MoveTowards(agent.acceleration, 10f, Time.deltaTime * 8f);
                    }
                    currentMonsterSpeed = Mathf.Clamp(currentMonsterSpeed, monsterBaseSpeed, monsterMaxSpeed);
                    agent.speed = currentMonsterSpeed;
                }
                
                float rawSpeed = (transform.position - previousPosition).magnitude / Time.deltaTime;
                averageVelocity = Mathf.Lerp(averageVelocity, rawSpeed, Time.deltaTime * 10f);
                previousPosition = transform.position;

                float speedMult = Mathf.Clamp(averageVelocity / 4.5f, 0.5f, 4f);
                if (creatureAnimator != null)
                {
                    creatureAnimator.SetFloat("monsterSpeedMult", speedMult);
                }
                
                // Use speed multiplyer from monster animator
                if (monsterModelContainer != null)
                {
                    Animator[] animators = monsterModelContainer.GetComponentsInChildren<Animator>(true);
                    foreach (Animator anim in animators)
                    {
                        anim.SetFloat("monsterSpeedMult", speedMult);
                    }
                }
            }

            timeSinceLastAttack += Time.deltaTime;
            chosenBloodTimer -= Time.deltaTime;
            if (!isConsumingBlood && !isWalkingIntoPortal && !isCurrentlyTransforming)
            {
                teleportCooldownTimer -= Time.deltaTime;
            }
            geyserCooldown -= Time.deltaTime;
            laughCooldown -= Time.deltaTime;

            if (!isEnemyDead && agent != null && agent.isOnNavMesh && currentBehaviourStateIndex != 3 && !isWalkingIntoPortal)
            {
                agent.speed = 3.5f;
            }

            if (footprintPool != null && footprintTimers != null)
            {
                for (int i = 0; i < footprintPool.Length; i++)
                {
                    if (footprintTimers[i] > 0f)
                    {
                        footprintTimers[i] -= Time.deltaTime;
                        if (footprintTimers[i] <= 0f && footprintPool[i] != null)
                        {
                            footprintPool[i].SetActive(false);
                        }
                    }
                }
            }

            if (activeFootprintTimer > 0f) activeFootprintTimer -= Time.deltaTime;

            if (activeFootprintTimer > 0f && !isEnemyDead && !isCurrentlyTransforming && !isConsumingBlood && footprintPool != null && footprintPool.Length > 0)
            {
                if (previousFootprintPosition == Vector3.zero) previousFootprintPosition = transform.position;
                
                float distMoved = Vector3.Distance(transform.position, previousFootprintPosition);
                accumulatedFootprintDistance += distMoved;
                previousFootprintPosition = transform.position;

                if (accumulatedFootprintDistance >= footprintDistanceThreshold)
                {
                    accumulatedFootprintDistance = 0f;
                    SpawnFootprint();
                }
            }
            else if (!isConsumingBlood)
            {
                previousFootprintPosition = transform.position;
            }

            if (isConsumingBlood)
            {
                movingTowardsTargetPlayer = false;
                targetPlayer = null;
                transform.position = consumptionLockPosition;
                serverPosition = consumptionLockPosition;

                if (IsServer && agent != null && agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }
                
                // Pause normal logic while consuming
                return;
            }

            // Limb Regeneration
            if (removableLimbs != null)
            {
                for (int i = 0; i < removableLimbs.Length; i++)
                {
                    if (isLimbDetached[i])
                    {
                        limbRegenTimers[i] -= Time.deltaTime;
                        if (limbRegenTimers[i] <= 0f)
                        {
                            isLimbDetached[i] = false;

                            if (i == 0 && bloodOrb != null) bloodOrb.SetActive(true);
                            if (i == 1 && dagger != null) dagger.SetActive(true);
                            if (i == 2 && CurrentLevel >= 2 && level2EyeEffects != null)
                            {
                                foreach (var eye in level2EyeEffects)
                                {
                                    if (eye != null) eye.SetActive(true);
                                }
                            }

                            if (removableLimbs[i] != null && removableLimbs[i].renderers != null)
                            {
                                foreach (var r in removableLimbs[i].renderers)
                                {
                                    if (r != null && r.materials != null)
                                    {
                                        foreach (Material mat in r.materials)
                                        {
                                            if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", 0.011f);
                                            
                                            Color c = mat.color;
                                            c.a = 1f;
                                            mat.color = c;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (removableLimbs[i] != null && removableLimbs[i].renderers != null)
                            {
                                float progress = limbRegenDuration > 0f ? 1f - (limbRegenTimers[i] / limbRegenDuration) : 1f;
                                foreach (var r in removableLimbs[i].renderers)
                                {
                                    if (r != null && r.materials != null)
                                    {
                                        foreach (Material mat in r.materials)
                                        {
                                            if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", Mathf.Lerp(1f, 0.011f, progress));
                                            
                                            Color c = mat.color;
                                            c.a = Mathf.Lerp(0f, 1f, progress);
                                            mat.color = c;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            bool leftArmSevered = (removableLimbs != null && isLimbDetached != null && isLimbDetached.Length > 0 && isLimbDetached[0]);
            
            if (IsServer && canTeleport && teleportCooldownTimer <= 0f && !isConsumingBlood && explosionTimer <= 0f && CurrentLevel < 4 && !leftArmSevered)
            {
                if (bloodTargetWaitTimer > 0f) bloodTargetWaitTimer -= Time.deltaTime;

                bloodSearchTimer -= Time.deltaTime;
                if (bloodSearchTimer <= 0f)
                {
                    bloodSearchTimer = 2f;
                    if (currentBloodTarget == null || !currentBloodTarget.gameObject.activeInHierarchy)
                    {
                        Transform newTarget = FindBestBloodSource();
                        if (newTarget != null && newTarget != currentBloodTarget)
                        {
                            currentBloodTarget = newTarget;
                            bloodTargetWaitTimer = 3f; // wait 3 seconds before engaging fresh blood
                        }
                    }
                    
                    if (currentBloodTarget != null && bloodTargetWaitTimer <= 0f)
                    {
                        FindNodeNearestToBlood(currentBloodTarget.position);
                    }
                }
            }
        }

        //container to interact witrh specific consume routine
        private Coroutine consumeCoroutine;
 
        private System.Collections.IEnumerator TeleportThenConsumeRoutine()
        {
            isConsumingBlood = true; // Pauses AI immediately
            movingTowardsTargetPlayer = false;

            if (IsServer)
            {
                if (currentSearch != null && currentSearch.inProgress)
                {
                    StopSearch(currentSearch);
                }
                SetDestinationToPosition(transform.position);

                if (agent != null)
                {
                    agent.speed = 0f;
                    agent.velocity = Vector3.zero;

                    if (agent.isOnNavMesh)
                    {
                        agent.isStopped = true;
                        agent.ResetPath();
                    }
                }
            }

            // Short delay so teleport state updates across network
            yield return new WaitForSeconds(0.1f);

            if (creatureAnimator != null)
            {
                creatureAnimator.SetBool("isConsuming", true);
            }
            if (consumeBloodSFX != null && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(consumeBloodSFX);
            }

            yield return new WaitForSeconds(consumeDuration - 0.2f);

            if (!isConsumingBlood) yield break; // Was interrupted by HitEnemy

            activeFootprintTimer = 20f;
                    
            int tempBlood = bloodConsumed;
            tempBlood++; // Simulating the next drink
            bool willTransform = tempBlood >= 9;
                    
            // If she IS NOT transforming, let her resume walking normally.
            if (!willTransform)
            {
                isConsumingBlood = false;
                if (creatureAnimator != null)
                {
                    creatureAnimator.SetBool("isConsuming", false);
                }
                if (IsServer && agent != null && agent.isOnNavMesh)
                {
                    agent.isStopped = false;
                }
            }
            else
            {
                // If she IS transforming, leave isConsumingBlood = true so she stays completely frozen
                isWaitingForLevelUp = true;
            }
                    
            StartCoroutine(DelayedConsumeBlood(0.5f));
            if (currentBloodTarget != null && !consumedBloodTargets.Contains(currentBloodTarget))
            {
                consumedBloodTargets.Add(currentBloodTarget);
                currentBloodTarget = null;
            }
        }

        private Transform FindBestBloodSource()
        {
            Transform bestSource = null;
            float closestDistance = float.MaxValue;
            Vector3 myPos = transform.position;

            LogIfDebugBuild("FindBestBloodSource: Searching for blood sources...");

            void CheckSource(Transform source)
            {
                if (source == null || !source.gameObject.activeInHierarchy) return;
                if (consumedBloodTargets.Contains(source)) return;
                float dist = Vector3.Distance(myPos, source.position);
                if (dist < closestDistance)
                {
                    closestDistance = dist;
                    bestSource = source;
                }
            }

            if (canUsePlayerBlood && StartOfRound.Instance != null && StartOfRound.Instance.allPlayerScripts != null)
            {
                foreach (PlayerControllerB p in StartOfRound.Instance.allPlayerScripts)
                {
                    if (p != null && p.playerBloodPooledObjects != null)
                    {
                        foreach (GameObject bloodObj in p.playerBloodPooledObjects)
                        {
                            if (bloodObj != null) CheckSource(bloodObj.transform);
                        }
                    }
                    
                    if (p != null && p.isPlayerDead && p.deadBody != null)
                    {
                        CheckSource(p.deadBody.transform);
                    }
                }
            }

            if (canUseEnemyBlood)
            {
                if (EnemyBloodPatch.enemyBloodDrops != null)
                {
                    foreach (GameObject eBlood in EnemyBloodPatch.enemyBloodDrops)
                    {
                        if (eBlood != null) CheckSource(eBlood.transform);
                    }
                }

                if (RoundManager.Instance != null && RoundManager.Instance.SpawnedEnemies != null)
                {
                    foreach (EnemyAI enemy in RoundManager.Instance.SpawnedEnemies)
                    {
                        if (enemy != null && enemy != this && enemy.isEnemyDead)
                        {
                            CheckSource(enemy.transform);
                        }
                    }
                }
            }

            if (bestSource != null)
            {
                LogIfDebugBuild($"FindBestBloodSource: Found best source at {bestSource.position} with distance {closestDistance}");
            }
            else
            {
                LogIfDebugBuild("FindBestBloodSource: No valid blood sources found.");
            }

            return bestSource;
        }

        private void FindNodeNearestToBlood(Vector3 bloodLocation)
        {
            if (!canTeleport) return;
            
            GameObject closestNode = null;
            float minDistance = float.MaxValue;
            bool nodeIsOutside = false;

            if (RoundManager.Instance != null)
            {
                if (RoundManager.Instance.insideAINodes != null)
                {
                    foreach (GameObject node in RoundManager.Instance.insideAINodes)
                    {
                        if (node == null) continue;
                        float dist = Vector3.Distance(node.transform.position, bloodLocation);
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            closestNode = node;
                            nodeIsOutside = false;
                        }
                    }
                }
                
                bool canUseOutsideNodes = (CurrentLevel < 4 && canGrannyGoOutside) || (CurrentLevel == 4 && canMonstGoOutside);

                if (canUseOutsideNodes && RoundManager.Instance.outsideAINodes != null)
                {
                    foreach (GameObject node in RoundManager.Instance.outsideAINodes)
                    {
                        if (node == null) continue;
                        float dist = Vector3.Distance(node.transform.position, bloodLocation);
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            closestNode = node;
                            nodeIsOutside = true;
                        }
                    }
                }
            }
            
            if (closestNode == null && allAINodes != null)
            {
                foreach (GameObject node in allAINodes)
                {
                    if (node == null) continue;
                    float dist = Vector3.Distance(node.transform.position, bloodLocation);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestNode = node;
                        nodeIsOutside = this.isOutside; // Default to current state
                    }
                }
            }

            if (closestNode != null)
            {
                if (CurrentLevel == 3) teleportCooldownTimer = Level3TPCooldown;
                else if (CurrentLevel == 2) teleportCooldownTimer = Level2TPCooldown;
                else teleportCooldownTimer = Level1TPCooldown;
                chosenTeleportNode = closestNode.transform.position;
                
                Vector3 finalTeleportPos = bloodLocation;
                if (UnityEngine.AI.NavMesh.SamplePosition(bloodLocation, out UnityEngine.AI.NavMeshHit hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    finalTeleportPos = hit.position;
                }
                else
                {
                    LogIfDebugBuild("Blood not on navmesh, chosing another spot");
                    finalTeleportPos = closestNode.transform.position;
                }

                LogIfDebugBuild($"Found closest node at {minDistance} units. Teleporting ontop of blood.");
                if (IsServer)
                {
                    if (portalCoroutine != null) StopCoroutine(portalCoroutine);
                    portalCoroutine = StartCoroutine(PortalSequenceRoutine(finalTeleportPos, nodeIsOutside));
                }
                else
                {
                    TeleportBWServerRpc(finalTeleportPos, nodeIsOutside);
                }
            }
            else
            {
                LogIfDebugBuild("No AI nodes found at all. Teleport cancelled");
            }
        }

        private Coroutine portalCoroutine;
        private GameObject entrancePortal;
        private GameObject exitPortal;

        [ClientRpc]
        public void SpawnEntrancePortalClientRpc(Vector3 position, Quaternion rotation)
        {
            if (portalPrefab != null)
            {
                entrancePortal = Instantiate(portalPrefab, position, rotation);
                ParticleSystem[] particles = entrancePortal.GetComponentsInChildren<ParticleSystem>();
                foreach (var p in particles) p.Play();
                UnityEngine.VFX.VisualEffect[] vfx = entrancePortal.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>();
                foreach (var v in vfx) v.Play();
            }
            if (portalStartSFX != null && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(portalStartSFX);
            }
        }

        [ClientRpc]
        public void SpawnExitPortalClientRpc(Vector3 position, Quaternion rotation)
        {
            if (portalPrefab != null)
            {
                exitPortal = Instantiate(portalPrefab, position, rotation);
                ParticleSystem[] particles = exitPortal.GetComponentsInChildren<ParticleSystem>();
                foreach (var p in particles) p.Play();
                UnityEngine.VFX.VisualEffect[] vfx = exitPortal.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>();
                foreach (var v in vfx) v.Play();
            }
        }

        [ClientRpc]
        public void ClosePortalsClientRpc()
        {
            if (portalCloseSFX != null && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(portalCloseSFX);
            }
            if (entrancePortal != null) Destroy(entrancePortal);
            if (exitPortal != null) Destroy(exitPortal);
        }

        private System.Collections.IEnumerator PortalSequenceRoutine(Vector3 bloodLocation, bool setOutside)
        {
            isWalkingIntoPortal = true;
            movingTowardsTargetPlayer = false;
            
            if (currentSearch != null && currentSearch.inProgress) StopSearch(currentSearch);

            Vector3 entrancePos = transform.position + transform.forward * 1.5f;
            entrancePos.y += 2f;
            
            SpawnEntrancePortalClientRpc(entrancePos, transform.rotation);
            
            Quaternion exitRot = Quaternion.Euler(90f, 0f, 0f);
            SpawnExitPortalClientRpc(bloodLocation, exitRot);

            float t = 0f;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = false;
                agent.SetDestination(entrancePos);
                agent.speed = 3f;
            }

            while (t < 1f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            
            TeleportBWClientRpc(bloodLocation, setOutside);
            isWalkingIntoPortal = false;

            yield return new WaitForSeconds(2f);
            ClosePortalsClientRpc();
        }

        //Local
        private void TeleportBloodWitch(Vector3 bloodLocation, bool setOutside)
        {
            movingTowardsTargetPlayer = false;
            if (currentSearch != null && currentSearch.inProgress) StopSearch(currentSearch);

            if (this.isOutside != setOutside)
            {
                SetEnemyOutside(setOutside);
            }

            if (IsServer && agent != null)
            {
                agent.Warp(bloodLocation);

                agent.isStopped = true;
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            else if (!IsServer)
            {
                transform.position = bloodLocation;
            }

            if (teleportSFX != null && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(teleportSFX);
            }
            serverPosition = bloodLocation;
            consumptionLockPosition = bloodLocation;
        }

        [ServerRpc(RequireOwnership = false)]
        public void TeleportBWServerRpc(Vector3 bloodLocation, bool setOutside)
        {
            if (portalCoroutine != null) StopCoroutine(portalCoroutine);
            portalCoroutine = StartCoroutine(PortalSequenceRoutine(bloodLocation, setOutside));
        }

        [ClientRpc]
        public void TeleportBWClientRpc(Vector3 bloodLocation, bool setOutside)
        {
            TeleportBloodWitch(bloodLocation, setOutside);
            hasTeleported = true;
        }

        private System.Collections.IEnumerator TransformationRoutine()
        {
            isConsumingBlood = false; // Release the freeze lock
            isWaitingForLevelUp = false;
            isCurrentlyTransforming = true;
            if (agent != null)
            {
                agent.speed = 0f;
                if (agent.isOnNavMesh) agent.isStopped = true;
            }
            if (creatureAnimator != null)
            {
                creatureAnimator.SetBool("isConsuming", false);
                creatureAnimator.SetBool("isTransforming", true);
            }
            if (transformationSFX != null && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(transformationSFX);
            }
            if (transformationSFXDistant != null && distantScreamAudioSource != null)
            {
                distantScreamAudioSource.clip = transformationSFXDistant;
                distantScreamAudioSource.Play();
            }
            yield return new WaitForSeconds(3f);
            
            if (BloodSpurtParticleBackL != null) BloodSpurtParticleBackL.Play();
            if (BloodSpurtParticleBackR != null) BloodSpurtParticleBackR.Play();

            yield return new WaitForSeconds(3f);
            
            if (grannyModelContainer != null) grannyModelContainer.SetActive(false);
            if (monsterModelContainer != null) monsterModelContainer.SetActive(true);
            
            if (IsServer && agent != null)
            {
                if (agent.isOnNavMesh) agent.isStopped = false;
                agent.speed = 4f; // Starts slow
                agent.acceleration = 2f; // reset acceleration so it ramps up
            }
            if (creatureAnimator != null)
            {
                creatureAnimator.SetBool("isTransforming", false);
            }
            isCurrentlyTransforming = false;
        }

        private System.Collections.IEnumerator DelayedConsumeBlood(float delay)
        {
            yield return new WaitForSeconds(delay);
            ConsumeBlood();
        }

        public void ConsumeBlood()
        {
            if (IsServer) 
            {
                int oldLevel = CurrentLevel;
                bloodConsumed++;
                int newLevel = CurrentLevel;
                ConsumeBloodClientRpc(bloodConsumed, oldLevel, newLevel);

                if (newLevel == 3) teleportCooldownTimer = Level3TPCooldown;
                else if (newLevel == 2) teleportCooldownTimer = Level2TPCooldown;
                else teleportCooldownTimer = Level1TPCooldown;
            }
        }

        [ClientRpc]
        public void ConsumeBloodClientRpc(int newBloodCount, int oldLevel, int newLevel)
        {
            bloodConsumed = newBloodCount;
            LogIfDebugBuild($"Consumed blood! Level is now: {CurrentLevel}");

            if (newLevel > oldLevel)
            {
                ApplyLevelMaterials(newLevel);
            }

            // Play granny scream only if not transforming to monster
            if (newLevel > oldLevel && newLevel < 4 && levelUpScreams != null && levelUpScreams.Length > 0 && screamAudioSource != null)
            {
                int index = UnityEngine.Random.Range(0, levelUpScreams.Length);
                screamAudioSource.PlayOneShot(levelUpScreams[index]);
                if (levelUpScreamsDistant != null && levelUpScreamsDistant.Length > index && distantScreamAudioSource != null)
                {
                    distantScreamAudioSource.clip = levelUpScreamsDistant[index];
                    distantScreamAudioSource.Play();
                }
            }
        }

        public void CastBloodGeyser(PlayerControllerB player)
        {
            if (!IsServer) return;
            
            // Cannot use Level 2 Geyser if left arm is severed
            if (CurrentLevel < 4 && removableLimbs != null && isLimbDetached != null && isLimbDetached.Length > 0 && isLimbDetached[0])
            {
                return;
            }

            LogIfDebugBuild("Casting blood geyser on " + player.playerUsername);
            
            SyncGeyserAnimationClientRpc();
            
            Vector3 predictedPos = player.transform.position;
            if (player == targetPlayer)
            {
                Vector3 vel = targetPlayerVelocity;
                if (vel.magnitude > 10f) vel = vel.normalized * 10f; 
                predictedPos += vel * 1.5f; 
            }
            else
            {
                predictedPos += player.playerBodyAnimator.transform.forward * 2f;
            }

            SpawnGeyserWarningClientRpc(predictedPos);
            StartCoroutine(GeyserAttackDelay(predictedPos));
        }

        [ClientRpc]
        public void SyncGeyserAnimationClientRpc()
        {
            if (creatureAnimator != null)
            {
                creatureAnimator.SetTrigger("useGeyser");
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void SpawnGeyserWarningServerRpc(Vector3 position)
        {
            SpawnGeyserWarningClientRpc(position);
        }

        [ClientRpc]
        public void SpawnGeyserWarningClientRpc(Vector3 position)
        {
            if (geyserPrefab != null)
            {
                currentActiveGeyser = Instantiate(geyserPrefab, position, Quaternion.identity);
                Transform warningAudioObj = currentActiveGeyser.transform.Find("Warning");
                if (warningAudioObj != null)
                {
                    AudioSource audio = warningAudioObj.GetComponent<AudioSource>();
                    if (audio != null) audio.Play();
                }
            }
        }

        private IEnumerator GeyserAttackDelay(Vector3 position)
        {
            yield return new WaitForSeconds(1.5f);
            SpawnGeyserAttackClientRpc(position);
            
            yield return new WaitForSeconds(2f);
            
            DestroyGeyserClientRpc();
        }

        [ClientRpc]
        public void SpawnGeyserAttackClientRpc(Vector3 position)
        {
            if (currentActiveGeyser != null)
            {
                Animator anim = currentActiveGeyser.GetComponentInChildren<Animator>();
                if (anim != null) anim.SetBool("Toggle Geyser", true);

                var vfx = currentActiveGeyser.GetComponentInChildren<UnityEngine.VFX.VisualEffect>();
                if (vfx != null) vfx.SetBool("Toggle Geyser", true);

                Transform castAudioObj = currentActiveGeyser.transform.Find("Cast");
                if (castAudioObj != null)
                {
                    AudioSource audio = castAudioObj.GetComponent<AudioSource>();
                    if (audio != null) audio.Play();
                }
            }

            StartCoroutine(ClientGeyserDamageRoutine(position));
        }

        private IEnumerator ClientGeyserDamageRoutine(Vector3 position)
        {
            float lingerTimer = 0f;
            while (lingerTimer < 2f)
            {
                if (GameNetworkManager.Instance != null && GameNetworkManager.Instance.localPlayerController != null)
                {
                    PlayerControllerB localPlayer = GameNetworkManager.Instance.localPlayerController;
                    if (!localPlayer.isPlayerDead && Vector3.Distance(localPlayer.transform.position, position) <= 3.5f)
                    {
                        localPlayer.DamagePlayer(15, hasDamageSFX: true, callRPC: true, CauseOfDeath.Unknown);
                    }
                }
                yield return new WaitForSeconds(0.25f);
                lingerTimer += 0.25f;
            }
        }

        [ClientRpc]
        public void DestroyGeyserClientRpc()
        {
            if (currentActiveGeyser != null)
            {
                Destroy(currentActiveGeyser);
                currentActiveGeyser = null;
            }
        }

        public override void SetEnemyStunned(bool setToStunned, float setToStunTime = 1f, PlayerControllerB setStunnedByPlayer = null)
        {
            if (CurrentLevel == 4) return; // Monster mode is completely invulnerable
            
            // Limit stun time to a max of 2.5 seconds to prevent extremely long stuns
            float adjustedStunTime = Mathf.Min(setToStunTime, 2.5f);
            
            base.SetEnemyStunned(setToStunned, adjustedStunTime, setStunnedByPlayer);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            if (CurrentLevel == 4) return; // Monster mode is completely invulnerable
            
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead) return;

            if (isWalkingIntoPortal)
            {
                isWalkingIntoPortal = false;
                if (portalCoroutine != null) StopCoroutine(portalCoroutine);
                ClosePortalsClientRpc();
            }

            if (isConsumingBlood)
            {
                isConsumingBlood = false;
                if (consumeCoroutine != null) StopCoroutine(consumeCoroutine);
                
                if (creatureAnimator != null)
                {
                    creatureAnimator.SetBool("isConsuming", false);
                }
                if (IsServer && agent != null && agent.isOnNavMesh)
                {
                    agent.isStopped = false;
                }
            }

            if (playerWhoHit != null && IsServer)
            {
                targetPlayer = playerWhoHit;
                movingTowardsTargetPlayer = true;
                if (currentSearch != null) StopSearch(currentSearch);
            }

            if (!isLimbDetached[2] && !isLimbDetached[3])
            {
                if (IsServer) PlayHitSFXClientRpc();
            }

            if (IsServer && removableLimbs != null)
            {
                currentLimbDamage += force;
                if (currentLimbDamage >= limbHealth)
                {
                    currentLimbDamage = 0;
                    List<int> attachedIndices = new List<int>();
                    for (int i = 0; i < isLimbDetached.Length; i++)
                    {
                        if (!isLimbDetached[i]) attachedIndices.Add(i);
                    }

                    if (attachedIndices.Count > 0)
                    {
                    List<int> selectableIndices = new List<int>();
                    
                    if (attachedIndices.Contains(0) || attachedIndices.Contains(1))
                    {
                        if (attachedIndices.Contains(0)) selectableIndices.Add(0);
                        if (attachedIndices.Contains(1)) selectableIndices.Add(1);
                    }
                    else if (attachedIndices.Contains(2))
                    {
                        selectableIndices.Add(2);
                    }
                    else if (attachedIndices.Contains(3))
                    {
                        selectableIndices.Add(3);
                    }

                    int indexToDetach = selectableIndices[UnityEngine.Random.Range(0, selectableIndices.Count)];
                    DetachLimbClientRpc(indexToDetach);
                    }
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void DetachLimbServerRpc(int limbIndex)
        {
            DetachLimbClientRpc(limbIndex);
        }

        [ClientRpc]
        public void DetachLimbClientRpc(int limbIndex)
        {
            if (isLimbDetached[limbIndex]) return;

            isLimbDetached[limbIndex] = true;
            limbRegenTimers[limbIndex] = limbRegenDuration;

            if (limbIndex == 0 && bloodOrb != null) bloodOrb.SetActive(false);
            if (limbIndex == 1 && dagger != null) dagger.SetActive(false);

            // Play blood spurt particles
            if (limbIndex == 0 && BloodSpurtParticleArmL != null) BloodSpurtParticleArmL.Play();
            else if (limbIndex == 1 && BloodSpurtParticleArmR != null) BloodSpurtParticleArmR.Play();
            else if (limbIndex == 2)
            {
                if (BloodSpurtParticleHead != null) BloodSpurtParticleHead.Play();
                if (level2EyeEffects != null)
                {
                    foreach (var eye in level2EyeEffects)
                    {
                        if (eye != null) eye.SetActive(false);
                    }
                }
            }
            else if (limbIndex == 3)
            {
                if (BloodSpurtParticleBackL != null) BloodSpurtParticleBackL.Play();
                if (BloodSpurtParticleBackR != null) BloodSpurtParticleBackR.Play();
            }

            if (removableLimbs[limbIndex] != null && removableLimbs[limbIndex].renderers != null && removableLimbs[limbIndex].renderers.Length > 0)
            {
                if (severedLimbPrefabs != null && severedLimbPrefabs.Length > limbIndex && severedLimbPrefabs[limbIndex] != null)
                {
                    Transform spawnTrans = removableLimbs[limbIndex].renderers[0].transform;
                    GameObject severedLimb = Instantiate(severedLimbPrefabs[limbIndex], spawnTrans.position, spawnTrans.rotation);
                    
                    MeshCollider mc = severedLimb.GetComponentInChildren<MeshCollider>();
                    if (mc != null) mc.convex = true;
                    
                    Rigidbody rb = severedLimb.GetComponentInChildren<Rigidbody>();
                    if (rb == null) rb = severedLimb.AddComponent<Rigidbody>();
                    rb.isKinematic = false;
                    rb.useGravity = true;

                    ParticleSystem[] particles = severedLimb.GetComponentsInChildren<ParticleSystem>();
                    foreach (var p in particles) p.Play();
                    
                    UnityEngine.VFX.VisualEffect[] vfx = severedLimb.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>();
                    foreach (var v in vfx) v.Play();

                    Destroy(severedLimb, 20f);
                }

                foreach (var r in removableLimbs[limbIndex].renderers)
                {
                    if (r != null && r.materials != null)
                    {
                        foreach (Material mat in r.materials)
                        {
                            if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", 1f);
                            
                            Color c = mat.color;
                            c.a = 0f;
                            mat.color = c;
                        }
                    }
                }
            }

            bool allDetached = true;
            for (int i = 0; i < isLimbDetached.Length; i++)
            {
                if (!isLimbDetached[i])
                {
                    allDetached = false;
                    break;
                }
            }

            if (allDetached)
            {
                KillEnemyOnOwnerClient();
            }
        }

        public void ExplodePlayer(PlayerControllerB player)
        {
            if (!IsServer) return;
            LogIfDebugBuild("Exploding player " + player.playerUsername);
            SpawnBloodExplosionClientRpc((int)player.playerClientId, player.transform.position);
        }

        [ServerRpc(RequireOwnership = false)]
        public void SpawnBloodExplosionServerRpc(int playerId, Vector3 pos)
        {
            SpawnBloodExplosionClientRpc(playerId, pos);
        }

        [ClientRpc]
        public void SpawnBloodExplosionClientRpc(int playerId, Vector3 pos)
        {
            if (playerBloodExplosionPrefab != null)
            {
                GameObject explosion = Instantiate(playerBloodExplosionPrefab, pos, Quaternion.identity);
                ParticleSystem[] pSystems = explosion.GetComponentsInChildren<ParticleSystem>();
                foreach (ParticleSystem ps in pSystems)
                {
                    ps.Play(true);
                }
            }
            if (bloodExplosionSFX != null)
            {
                AudioSource.PlayClipAtPoint(bloodExplosionSFX, pos, 1f);
            }

            if (StartOfRound.Instance != null)
            {
                PlayerControllerB target = StartOfRound.Instance.allPlayerScripts[playerId];
                if (target != null)
                {
                    if (target == GameNetworkManager.Instance.localPlayerController && !target.isPlayerDead)
                    {
                        target.DamagePlayer(100, hasDamageSFX: true, callRPC: true, CauseOfDeath.Blast, deathAnimation: -1);
                    }

                    if (!target.isPlayerDead || (target.isPlayerDead && target.deadBody != null))
                    {
                        for (int i = 0; i < 20; i++)
                        {
                            Vector3 randomDirection = UnityEngine.Random.onUnitSphere;
                            randomDirection.y = Mathf.Abs(randomDirection.y); 
                            target.DropBlood(randomDirection, leaveBlood: true, leaveFootprint: false);
                        }
                    }
                }
            }
        }

        public override void OnCollideWithPlayer(UnityEngine.Collider other)
        {
            base.OnCollideWithPlayer(other);
            if (isEnemyDead || isConsumingBlood) return;

            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other);
            if (player != null && timeSinceLastAttack >= grannyAttackCooldownThreshold)
            {
                // Index 1 = right arm
                if (removableLimbs != null && isLimbDetached != null && isLimbDetached.Length > 1 && isLimbDetached[1])
                {
                    return; // Cannot attack if right arm is missing
                }

                timeSinceLastAttack = 0f;

                if (CurrentLevel < 4)
                {
                    // Level 1-3 Dagger attack
                    player.DamagePlayer(20, hasDamageSFX: true, callRPC: true, CauseOfDeath.Stabbing);
                    
                    if (IsServer) PlayStabSFXClientRpc();
                    else PlayStabSFXServerRpc();

                    if (laughCooldown <= 0f)
                    {
                        if (CurrentLevel == 3 && level3LaughingClips != null && level3LaughingClips.Length > 0)
                        {
                            int index = enemyRandom.Next(0, level3LaughingClips.Length);
                            if (IsServer) PlayLaughClientRpc(index, true);
                            else PlayLaughServerRpc(index, true);
                            laughCooldown = 1f;
                        }
                        else if (laughingClips != null && laughingClips.Length > 0)
                        {
                            int index = enemyRandom.Next(0, laughingClips.Length);
                            if (IsServer) PlayLaughClientRpc(index, false);
                            else PlayLaughServerRpc(index, false);
                            laughCooldown = 1f;
                        }
                    }
                }
                else 
                {
                    // Level 4 Monster attack
                    if (creatureAnimator != null)
                    {
                        creatureAnimator.SetTrigger("MonsterAttack");
                    }
                    if (monsterModelContainer != null)
                    {
                        Animator[] animators = monsterModelContainer.GetComponentsInChildren<Animator>(true);
                        foreach (Animator anim in animators)
                        {
                            anim.SetTrigger("MonsterAttack");
                        }
                    }

                    if (IsServer) PlayMonsterAttackSFXClientRpc();
                    else PlayMonsterAttackSFXServerRpc();
                    
                    StartCoroutine(PauseAfterAttack(3f));
                      
                    bool willDie = player.health - 40 <= 0 || player.isPlayerDead;
                    player.DamagePlayer(40, hasDamageSFX: true, callRPC: true, CauseOfDeath.Mauling);
                      
                    if (IsServer) PlayMonsterHitPlayerSFXClientRpc();
                    else PlayMonsterHitPlayerSFXServerRpc();
                      
                    if (willDie)
                    {
                        if (IsServer) SpawnBloodExplosionClientRpc((int)player.playerClientId, player.transform.position);
                        else SpawnBloodExplosionServerRpc((int)player.playerClientId, player.transform.position);
                    }
                }
            }
        }

        private System.Collections.IEnumerator PauseAfterAttack(float duration)
        {
            isPausedAfterAttack = true;
            agent.speed = 0f;
            if (agent.isOnNavMesh) agent.isStopped = true;
            
            yield return new WaitForSeconds(duration);
            
            if (!isEnemyDead)
            {
                isPausedAfterAttack = false;
                if (agent.isOnNavMesh) agent.isStopped = false;
            }
        }

        [ClientRpc]
        public void TriggerTransformationClientRpc()
        {
            hasStartedTransformation = true;
            StartCoroutine(TransformationRoutine());
        }

        [ClientRpc]
        public void SyncBoilingAnimationClientRpc(bool isBoiling, bool exploded)
        {
            if (creatureAnimator != null)
            {
                creatureAnimator.SetBool("isBoiling", isBoiling);
                if (isBoiling)
                {
                    creatureAnimator.SetTrigger("startBoiling");
                }
                
                if (exploded)
                {
                    creatureAnimator.SetBool("explodedPlayer", true);
                }
                else if (!isBoiling)
                {
                    creatureAnimator.SetBool("explodedPlayer", false);
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void PlayStabSFXServerRpc()
        {
            PlayStabSFXClientRpc();
        }

        [ClientRpc]
        public void PlayStabSFXClientRpc()
        {
            if (creatureAnimator != null)
            {
                creatureAnimator.SetTrigger("hasStabbed");
            }
            if (stabSFX != null && daggerAudioSource != null)
            {
                daggerAudioSource.PlayOneShot(stabSFX);
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            base.KillEnemy(destroy);
            if (creatureAnimator != null)
            {
                creatureAnimator.SetBool("hasDied", true);
            }
            if (grannyAudioAnimationEvent != null)
            {
                grannyAudioAnimationEvent.enableAudio = false;
            }
            if (monsterAudioAnimationEvent != null)
            {
                monsterAudioAnimationEvent.enableAudio = false;
            }
            if (breathingAudioSource != null) breathingAudioSource.Stop();
            if (creatureVoice != null) creatureVoice.Stop();
            if (creatureSFX != null) creatureSFX.Stop();
            if (boil2DAudioSource != null) boil2DAudioSource.Stop();

            if (removableLimbs != null)
            {
                for (int i = 0; i < removableLimbs.Length; i++)
                {
                    if (isLimbDetached != null && isLimbDetached.Length > i && isLimbDetached[i] && removableLimbs[i] != null && removableLimbs[i].renderers != null)
                    {
                        foreach (var r in removableLimbs[i].renderers)
                        {
                            if (r != null && r.materials != null)
                            {
                                foreach (Material mat in r.materials)
                                {
                                    if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", 1f);
                                    if (mat.HasProperty("_Color")) 
                                    {
                                        Color c = mat.color;
                                        c.a = 0f;
                                        mat.color = c;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (screamAudioSource != null) screamAudioSource.Stop();
        }

        [ServerRpc(RequireOwnership = false)]
        public void PlayMonsterAttackSFXServerRpc()
        {
            PlayMonsterAttackSFXClientRpc();
        }

        [ClientRpc]
        public void PlayMonsterAttackSFXClientRpc()
        {
            if (monsterAttackSFX != null && monsterAttackSFX.Length > 0 && creatureSFX != null)
            {
                creatureSFX.PlayOneShot(monsterAttackSFX[UnityEngine.Random.Range(0, monsterAttackSFX.Length)]);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void PlayMonsterHitPlayerSFXServerRpc()
        {
            PlayMonsterHitPlayerSFXClientRpc();
        }

        [ClientRpc]
        public void PlayMonsterHitPlayerSFXClientRpc()
        {
            if (monsterHitPlayerSFX != null && monsterHitPlayerSFX.Length > 0 && creatureSFX != null)
            {
                creatureSFX.PlayOneShot(monsterHitPlayerSFX[UnityEngine.Random.Range(0, monsterHitPlayerSFX.Length)]);
            }
        }

        [ClientRpc]
        public void PlayMonsterSeePlayerSFXClientRpc()
        {
            if (monsterSeePlayerSFX != null && monsterSeePlayerSFX.Length > 0 && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(monsterSeePlayerSFX[UnityEngine.Random.Range(0, monsterSeePlayerSFX.Length)]);
            }
        }

        [ClientRpc]
        public void PlayHitSFXClientRpc()
        {
            if (hitSFX != null && hitSFX.Length > 0 && creatureVoice != null)
            {
                creatureVoice.PlayOneShot(hitSFX[UnityEngine.Random.Range(0, hitSFX.Length)]);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void PlayLaughServerRpc(int index, bool level3)
        {
            PlayLaughClientRpc(index, level3);
        }

        [ClientRpc]
        public void PlayLaughClientRpc(int clipIndex, bool isLevel3)
        {
            if (isLevel3)
            {
                if (level3LaughingClips != null && clipIndex >= 0 && clipIndex < level3LaughingClips.Length && creatureVoice != null)
                {
                    creatureVoice.PlayOneShot(level3LaughingClips[clipIndex]);
                }
            }
            else
            {
                if (laughingClips != null && clipIndex >= 0 && clipIndex < laughingClips.Length && creatureVoice != null)
                {
                    creatureVoice.PlayOneShot(laughingClips[clipIndex]);
                }
            }
        }

        [ClientRpc]
        public void SyncBoilTargetClientRpc(int playerId, float explosionTimerValue)
        {
            if (boil2DAudioSource == null) return;
            
            if (playerId == -1)
            {
                if (boil2DAudioSource.isPlaying) boil2DAudioSource.Stop();
                boil2DAudioSource.pitch = 1f;
                return;
            }
            
            // Only play if the local player is the target
            if (GameNetworkManager.Instance.localPlayerController != null && 
                (int)GameNetworkManager.Instance.localPlayerController.playerClientId == playerId)
            {
                if (!boil2DAudioSource.isPlaying && boilTarget2DSFX != null)
                {
                    boil2DAudioSource.Play();
                }
                
                // Pitch goes up as explosionTimer reaches explosionThreshold
                float maxTime = explosionThreshold;
                float progress = Mathf.Clamp01(explosionTimerValue / maxTime);
                boil2DAudioSource.pitch = 1f + (progress * 1.5f); // Pitch from 1.0 to 2.5
            }
        }

        public static Vector3? GetLatestBloodDropLocation(PlayerControllerB player)
        {
            if (player == null) return null;
            List<GameObject> bloodObjects = player.playerBloodPooledObjects;

            if (bloodObjects == null || bloodObjects.Count == 0 || currentBloodIndexField == null)
            {
                return null;
            }
            int nextIndex = (int)currentBloodIndexField.GetValue(player);
            int latestIndex = (nextIndex - 1 + bloodObjects.Count) % bloodObjects.Count;
            GameObject latestBlood = bloodObjects[latestIndex];
            if (latestBlood != null && latestBlood.activeSelf)
            {
                return latestBlood.transform.position;
            }
            return null;
        }

        private EntranceTeleport GetClosestDoorToMonster()
        {
            if (allTeleports == null || allTeleports.Length == 0)
            {
                allTeleports = FindObjectsOfType<EntranceTeleport>();
            }

            EntranceTeleport closestDoor = null;
            float minDist = float.MaxValue;

            foreach (EntranceTeleport door in allTeleports)
            {
                if (door.isEntranceToBuilding == this.isOutside)
                {
                    float dist = Vector3.Distance(transform.position, door.transform.position);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closestDoor = door;
                    }
                }
            }
            return closestDoor;
        }

        private EntranceTeleport GetCorrespondingDoor(EntranceTeleport chaserDoor)
        {
            if (chaserDoor == null) return null;
            if (allTeleports == null || allTeleports.Length == 0)
            {
                allTeleports = FindObjectsOfType<EntranceTeleport>();
            }

            foreach (EntranceTeleport door in allTeleports)
            {
                if (door.entranceId == chaserDoor.entranceId && door.isEntranceToBuilding != chaserDoor.isEntranceToBuilding)
                {
                    return door;
                }
            }
            return null;
        }

        [ServerRpc]
        public void TeleportEnemyServerRpc(Vector3 pos, bool setOutside)
        {
            TeleportEnemyClientRpc(pos, setOutside);
        }

        [ClientRpc]
        public void TeleportEnemyClientRpc(Vector3 pos, bool setOutside)
        {
            TeleportEnemyLocally(pos, setOutside);
        }

        private void TeleportEnemyLocally(Vector3 pos, bool setOutside)
        {
            if (agent != null) agent.enabled = false;
            transform.position = pos;
            if (agent != null) agent.enabled = true;
            serverPosition = pos;
            SetEnemyOutside(setOutside);
            
            //FindMainEntrancePosition handles syncing mainEntrancePosition
            if (RoundManager.Instance != null)
            {
                EntranceTeleport door = RoundManager.FindMainEntranceScript(setOutside);
                if (door != null && door.doorAudios != null && door.doorAudios.Length > 0 && door.entrancePointAudio != null)
                {
                    door.entrancePointAudio.PlayOneShot(door.doorAudios[0]);
                }
            }
        }

        private void HandleShipLeft()
        {
            LogIfDebugBuild("Ship is leaving! Cleaning up BloodWitch.");

            if (creatureVoice != null) creatureVoice.Stop();
            if (daggerAudioSource != null) daggerAudioSource.Stop();
            if (breathingAudioSource != null) breathingAudioSource.Stop();
            if (screamAudioSource != null) screamAudioSource.Stop();
            if (distantScreamAudioSource != null) distantScreamAudioSource.Stop();
            if (boil2DAudioSource != null) boil2DAudioSource.Stop();

            if (entrancePortal != null) Destroy(entrancePortal);
            if (exitPortal != null) Destroy(exitPortal);

            if (bloodOrb != null) bloodOrb.SetActive(false);

            KillEnemy(true);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();

            if (creatureVoice != null) creatureVoice.Stop();
            if (daggerAudioSource != null) daggerAudioSource.Stop();
            if (breathingAudioSource != null) breathingAudioSource.Stop();
            if (screamAudioSource != null) screamAudioSource.Stop();
            if (distantScreamAudioSource != null) distantScreamAudioSource.Stop();
            if (boil2DAudioSource != null) boil2DAudioSource.Stop();

            LogIfDebugBuild("OnDestroy successful.");
        }
    }
}
