using System;
using UnityEngine;

namespace CathayCrossing.Core
{
    public interface IMiniGame
    {
        string MiniGameId { get; }
        void Begin(MiniGameContext context);
    }

    public readonly struct MiniGameContext
    {
        public readonly GameObject Launcher;
        public readonly Action<MiniGameResult> OnComplete;

        public MiniGameContext(GameObject launcher, Action<MiniGameResult> onComplete)
        {
            Launcher = launcher;
            OnComplete = onComplete;
        }
    }

    public readonly struct MiniGameResult
    {
        public readonly bool Success;
        public readonly int Score;

        public MiniGameResult(bool success, int score)
        {
            Success = success;
            Score = score;
        }
    }
}
