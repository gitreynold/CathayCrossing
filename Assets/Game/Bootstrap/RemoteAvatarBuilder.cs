using CathayCrossing.Characters;
using CathayCrossing.HD2D;
using UnityEngine;

namespace CathayCrossing.Bootstrap
{
    /// <summary>
    /// 遠端玩家的正式外觀工廠。照 OfficePlayerSpawner 的
    /// CharacterDefinition 流程生成帶 Animator 的角色，讓遠端玩家的
    /// 走路 / WAVE / DANCE / SIT 動畫都看得到。
    ///
    /// NetworkManager（HD2D.Runtime 組件）不能反向依賴 Bootstrap 組件，
    /// 所以 NetworkChatBootstrap 啟動時把 Build 注入
    /// NetworkManager.RemoteAvatarBuilder 靜態掛勾。
    ///
    /// 外觀目前一律用 catalog 的 base 角色（大家長一樣）——
    /// 每位玩家自己的造型要等 appearance 同步（join 時廣播外觀 DTO）
    /// 之後才能各自長對，先不做。
    /// </summary>
    public static class RemoteAvatarBuilder
    {
        public static GameObject Build(Vector3 pos)
        {
            CharacterDefinition def = ResolveDefinition();
            if (def == null || def.body == null) return null; // 讓呼叫端退回膠囊

            var root = new GameObject();
            root.transform.position = pos;

            // 跟本地玩家同構：root → SpriteRoot（轉向）→ Body → Visual(FBX)
            var spriteRoot = new GameObject("SpriteRoot");
            spriteRoot.transform.SetParent(root.transform, false);
            var body = new GameObject("Body");
            body.transform.SetParent(spriteRoot.transform, false);

            var visual = Object.Instantiate(def.body, body.transform);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            PostureCorrection.Attach(visual, "Spine", def.spineCorrectionEuler);

            var anim = visual.GetComponentInChildren<Animator>();
            if (anim == null) anim = visual.AddComponent<Animator>();
            if (def.controller != null) anim.runtimeAnimatorController = def.controller;
            anim.applyRootMotion = false;

            // 遠端模式的控制器：isLocalPlayer=false → UpdateRemotePlayer()
            // 用 targetPosition/Rotation 平滑插值，速度回饋給 Animator。
            var ctrl = root.AddComponent<OctopathPlayerController>();
            ctrl.isLocalPlayer = false;
            ctrl.spriteRoot = spriteRoot.transform;
            ctrl.spriteVisual = null; // 真動畫由 Animator 驅動，不用 bob
            ctrl.animator = anim;
            ctrl.targetPosition = pos;

            return root;
        }

        // 遠端玩家一律用 catalog base 角色（同一副 rig / controller），
        // 不讀本地玩家的自訂造型 —— 那是「我」的樣子，不是對方的。
        static CharacterDefinition ResolveDefinition()
        {
            var all = Resources.LoadAll<CharacterDefinition>("Characters");
            if (all == null || all.Length == 0) return null;

            var catalog = Resources.Load<CharacterPartCatalog>("CharacterPartCatalog");
            if (catalog != null && !string.IsNullOrEmpty(catalog.baseCharacterId))
            {
                foreach (var def in all)
                {
                    if (def != null && def.id == catalog.baseCharacterId) return def;
                }
            }

            System.Array.Sort(all, (a, b) => string.CompareOrdinal(a.name, b.name));
            return all[0];
        }
    }
}
