using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameNetcodeStuff;
using LethalAvatars.Libs;
using LethalAvatars.Networking.Messages;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Avatar = LethalAvatars.SDK.Avatar;
using Object = UnityEngine.Object;

namespace LethalAvatars;

public static class PlayerAvatarAPI
{
    public class BundledAvatarData
    {
        public readonly AssetBundle bundle;
        public readonly Avatar avatar;

        public BundledAvatarData(AssetBundle bundle, Avatar bundleAvatar)
        {
            this.bundle = bundle;
            this.avatar = bundleAvatar;
        }
    }

    public class PlayerAvatarData
    {
        public readonly BundledAvatarData bundledData;
        public readonly PlayerControllerB player;
        public readonly Avatar instanceAvatar;

        public PlayerAvatarData(BundledAvatarData bundledData, PlayerControllerB player, Avatar instanceAvatar)
        {
            this.bundledData = bundledData;
            this.player = player;
            this.instanceAvatar = instanceAvatar;
        }
    }

    private static List<PlayerAvatarData> registeredAvatars = new();
    public static Dictionary<PlayerControllerB, PlayerAvatarData> RegisteredAvatars => registeredAvatars.ToDictionary(x => x.player, x => x);

    internal static Dictionary<string, string> cachedAvatarHashes = new();

    private static Dictionary<string, BundledAvatarData> cachedAvatars = new();
    public static List<BundledAvatarData> LoadedAvatars => new(cachedAvatars.Values);

    /// <summary>
    /// Gets the current LocalPlayer. Null if no LocalPlayer exists or if not in the GameScene.
    /// </summary>
    public static PlayerControllerB? LocalPlayer => GameNetworkManager.Instance.localPlayerController;

    private static Terminal? terminal;
    public static Terminal? Terminal
    {
        get
        {
            if (terminal == null)
                terminal = Object.FindObjectOfType<Terminal>();
            return terminal;
        }
    }

    public static PlayerControllerB[] GetAllPlayers() => Object.FindObjectsOfType<PlayerControllerB>();

    public static bool TryGetRegisteredInstancedAvatar(PlayerControllerB player, out Avatar avatar)
    {
        #nullable disable
        avatar = null;
        #nullable restore
        if (!RegisteredAvatars.TryGetValue(player, out PlayerAvatarData data))
            return false;
        avatar = data.instanceAvatar;
        return avatar != null;
    }

    private static BundledAvatarData? LoadAvatar(AssetBundle assetBundle, string assetBundleHash)
    {
        Avatar avatar;
        try
        {
            avatar = assetBundle.LoadAllAssets<GameObject>().First().GetComponent<Avatar>();
        }
        catch (Exception)
        {
            // Probably failed to find the avatar
            assetBundle.Unload(true);
            return null;
        }
        BundledAvatarData data = new(assetBundle, avatar);
        cachedAvatars.Add(assetBundleHash, data);
        return data;
    }

    /// <summary>
    /// Loads an Avatar AssetBundle from file path
    /// </summary>
    /// <param name="fileLocation">The file to load</param>
    /// <returns>Nullable BundledAvatarData; null if failed to load</returns>
    public static BundledAvatarData? LoadAvatar(string fileLocation)
    {
        string hash = Extensions.GetHashOfFile(fileLocation);
        if (cachedAvatars.TryGetValue(hash, out BundledAvatarData loadedAvatar))
        {
            // TODO: is this needed?
            if(loadedAvatar.avatar == null)
            {
                cachedAvatars.Remove(hash);
                return null;
            }
            return loadedAvatar;
        }
        try
        {
            AssetBundle assetBundle = AssetBundle.LoadFromFile(fileLocation);
            return assetBundle == null ? null : LoadAvatar(assetBundle, hash);
        }
        catch (Exception)
        {
            Plugin.PluginLogger.LogError($"Failed to load AssetBundle at {fileLocation}");
            return null;
        }
    }

    /// <summary>
    /// Loads an Avatar AssetBundle from memory
    /// </summary>
    /// <param name="avatarData">AssetBundle data</param>
    /// <returns>Nullable Avatar; null if failed to load</returns>
    public static BundledAvatarData? LoadAvatar(byte[] avatarData)
    {
        string hash = Extensions.GetHashOfData(avatarData);
        if (cachedAvatars.TryGetValue(hash, out BundledAvatarData loadedAvatar))
            return loadedAvatar;
        try
        {
            AssetBundle assetBundle = AssetBundle.LoadFromMemory(avatarData);
            return assetBundle == null ? null : LoadAvatar(assetBundle, hash);
        }
        catch (Exception)
        {
            Plugin.PluginLogger.LogError("Failed to load AssetBundle from Memory");
            return null;
        }
    }

    private static List<AnimatorPlayer> InitializeAnimatorControllers(Avatar avatar)
    {
        List<AnimatorPlayer> animatorControllers = new List<AnimatorPlayer>();
        Animator animator = avatar.GetComponent<Animator>();
        if (animator == null) return animatorControllers;
        foreach (RuntimeAnimatorController animatorController in avatar.Animators)
        {
            PlayableGraph playableGraph;
            AnimatorControllerPlayable controllerPlayable =
                AnimationPlayableUtilities.PlayAnimatorController(animator, animatorController,
                    out playableGraph);
            animatorControllers.Add(new AnimatorPlayer(avatar, animatorController, controllerPlayable,
                playableGraph));
        }
        return animatorControllers;
    }

    /// <summary>
    /// Applies an Avatar to a player's model
    /// </summary>
    /// <param name="clonedAvatar">The BundledAvatarData to use.</param>
    /// <param name="player">The player to apply the Avatar to</param>
    /// <param name="hash">The MD5 hash of the data for the Avatar</param>
    public static void ApplyNewAvatar(BundledAvatarData avatarData, PlayerControllerB player, string hash)
    {
        // Disable and rename old stuff
        Transform scav = player.transform.Find("ScavengerModel");
        for (int i = 0; i < scav.childCount; i++)
        {
            Transform child = scav.GetChild(i);
            if(!child.gameObject.name.Contains("LOD")) continue;
            child.gameObject.GetComponent<SkinnedMeshRenderer>().enabled = false;
            child.gameObject.SetActive(false);
        }
        Transform metarig = scav.Find("metarig");
        Transform armsMetaRig = metarig.Find("ScavengerModelArmsOnly/Circle");
        armsMetaRig.GetComponent<SkinnedMeshRenderer>().enabled = false;
        Transform spine003 = metarig.Find("spine/spine.001/spine.002/spine.003");
        spine003.Find("LevelSticker").gameObject.SetActive(false);
        spine003.Find("BetaBadge").gameObject.SetActive(false);
        // create new clone of avatar
        Avatar clonedAvatar = Object.Instantiate(avatarData.avatar.gameObject).GetComponent<Avatar>();
        // Add new stuff
        clonedAvatar.gameObject.name = "avatar";
        clonedAvatar.transform.SetParent(metarig.parent);
        clonedAvatar.transform.SetLocalPositionAndRotation(Vector3.zero, new Quaternion(0, 0, 0, 0));
        AvatarDriver avatarDriver = clonedAvatar.gameObject.AddComponent<AvatarDriver>();
        avatarDriver.player = player;
        avatarDriver.Avatar = clonedAvatar;
        avatarDriver.animators = InitializeAnimatorControllers(clonedAvatar);
        avatarDriver.lastLocalItemGrab = player.localItemHolder;
        avatarDriver.lastServerItemGrab = player.serverItemHolder;
        Transform cameraTransform = metarig.Find("CameraContainer/MainCamera");
        avatarDriver.AnimationDone(clonedAvatar.GetComponent<Animator>(), cameraTransform, player.IsLocal());
        PlayerAvatarData data = new(avatarData, player, clonedAvatar);
        registeredAvatars.Add(data);
        if (cachedAvatarHashes.ContainsKey(player.GetIdentifier()))
            cachedAvatarHashes[player.GetIdentifier()] = hash;
        else
            cachedAvatarHashes.Add(player.GetIdentifier(), hash);
        if(!player.IsLocal()) return;
        GameObject systemsObject = SceneManager.GetActiveScene().GetRootGameObjects().First(x => x.name == "Systems");
        Transform helmet = systemsObject.transform.Find("Rendering/PlayerHUDHelmetModel/ScavengerHelmet");
        RenderSpecificCamera renderSpecificCamera = helmet.gameObject.GetComponent<RenderSpecificCamera>();
        if (renderSpecificCamera == null)
            renderSpecificCamera = helmet.gameObject.AddComponent<RenderSpecificCamera>();
        Camera targetCamera = cameraTransform.GetComponent<Camera>();
        renderSpecificCamera.RenderCameras.Add(targetCamera);
        renderSpecificCamera.TargetRenderer = helmet.GetComponent<MeshRenderer>();
        renderSpecificCamera.OnHide += (camera, renderer) =>
        {
            if (camera != targetCamera) return;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        };
        renderSpecificCamera.OnShow += (camera, renderer) =>
        {
            if (camera != targetCamera) return;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        };
    }

    /// <summary>
    /// Reverts all modified objects when applying an Avatar to a player
    /// </summary>
    /// <param name="player">The player to revert</param>
    public static void ResetPlayer(PlayerControllerB player)
    {
        Transform scav = player.transform.Find("ScavengerModel");
        if(scav == null)
        {
            Plugin.PluginLogger.LogWarning("Could not find ScavengerModel! Cannot ResetPlayer.");
            return;
        }
        for (int i = 0; i < scav.childCount; i++)
        {
            Transform child = scav.GetChild(i);
            if(!child.gameObject.name.Contains("LOD")) continue;
            child.gameObject.GetComponent<SkinnedMeshRenderer>().enabled = true;
            child.gameObject.SetActive(true);
        }
        for (int i = 0; i < scav.childCount; i++)
        {
            Transform avatar = scav.GetChild(i);
            if(avatar.GetComponent<Avatar>() == null) continue;
            Object.DestroyImmediate(avatar.gameObject);
            registeredAvatars.Remove(RegisteredAvatars[player]);
            if(player != LocalPlayer) return;
            Transform metarig = scav.Find("metarig");
            Transform armsMetaRig = metarig.Find("ScavengerModelArmsOnly/Circle");
            armsMetaRig.GetComponent<SkinnedMeshRenderer>().enabled = true;
            Transform mainCamera = metarig.Find("CameraContainer/MainCamera");
            mainCamera.localPosition = Vector3.zero;
            GameObject systemsObject =
                SceneManager.GetActiveScene().GetRootGameObjects().First(x => x.name == "Systems");
            Transform helmet = systemsObject.transform.Find("Rendering/PlayerHUDHelmetModel/ScavengerHelmet");
            RenderSpecificCamera renderSpecificCamera = helmet.gameObject.GetComponent<RenderSpecificCamera>();
            if (renderSpecificCamera != null)
                Object.DestroyImmediate(renderSpecificCamera);
        }
        UnloadUnusedBundles();
    }

    public static void UnloadUnusedBundles()
    {
        List<string> toBeRemoved = new();
        registeredAvatars.RemoveAll(x => x.player == null);
        foreach (KeyValuePair<string, BundledAvatarData> dataKvp in cachedAvatars)
        {
            if (!registeredAvatars.Any(x => x.bundledData == dataKvp.Value))
            {
                Plugin.PluginLogger.LogDebug($"{dataKvp.Value.avatar.AvatarName} unloading...");
                dataKvp.Value.bundle.Unload(true);
                toBeRemoved.Add(dataKvp.Key);
            }
            else
            {
                Plugin.PluginLogger.LogDebug($"{dataKvp.Value.avatar.AvatarName} not unloaded.");
            }
        }
        foreach (string remove in toBeRemoved)
        {
            cachedAvatars.Remove(remove);
        }
    }
    
    public static void Reset()
    {
        foreach (PlayerControllerB player in GetAllPlayers())
        {
            if(!RegisteredAvatars.ContainsKey(player)) continue;
            try{ResetPlayer(player);}catch(Exception){}
        }
        foreach (PlayerAvatarData data in registeredAvatars)
        {
            Plugin.PluginLogger.LogDebug($"{data.bundledData.avatar.AvatarName} unloading...");
            data.bundledData.bundle.Unload(true);
        }
        registeredAvatars.Clear();
        terminal = null;
    }

    internal static void RefreshAllAvatars()
    {
        foreach (PlayerControllerB player in GetAllPlayers())
        {
            // No avatar in the first place
            if(!RegisteredAvatars.ContainsKey(player))
                continue;
            // No refresh needed if we still have an avatar
            if (RegisteredAvatars.ContainsKey(player) && RegisteredAvatars[player].instanceAvatar != null)
                continue;
            if(cachedAvatarHashes.ContainsKey(player.GetIdentifier()))
            {
                string hash = cachedAvatarHashes[player.GetIdentifier()];
                string file = String.Empty;
                foreach (string asset in Directory.GetFiles(Plugin.AvatarsPath))
                {
                    if (!Path.GetExtension(asset).Contains("lca")) continue;
                    string h = Extensions.GetHashOfFile(asset);
                    if (h != hash) continue;
                    file = asset;
                    break;
                }
                if (!string.IsNullOrEmpty(file))
                {
                    BundledAvatarData? avatar = LoadAvatar(file);
                    if (avatar?.avatar != null)
                        ApplyNewAvatar(avatar, player, hash);
                    continue;
                }
            }
            byte[]? data = null;
            foreach (KeyValuePair<string,byte[]> keyValuePair in AvatarData.cachedAvatarData)
            {
                if(keyValuePair.Key != player.GetIdentifier()) continue;
                data = keyValuePair.Value;
            }
            if (data != null)
            {
                BundledAvatarData? avatar = LoadAvatar(data);
                if(avatar?.avatar != null)
                    ApplyNewAvatar(avatar, player, Extensions.GetHashOfData(data));
                continue;
            }
            Plugin.PluginLogger.LogDebug($"Failed to find cached avatar hash for {player.GetIdentifier()}");
        }
        UnloadUnusedBundles();
    }
}