using System;
using UnityEngine;

namespace AutoLevel
{

    public abstract class BaseLevelDataBuilder : IDisposable
    {
        public Transform root;

        protected LevelData levelData;
        protected BlocksRepo.Runtime repo;

        protected BaseLevelDataBuilder(LevelData levelData,
        BlocksRepo.Runtime blockRepo, Transform extRoot = null)
        {
            this.repo = blockRepo;
            this.levelData = levelData;
            //root = new GameObject("root").transform;
            this.root = extRoot != null ? extRoot : new GameObject("root").transform;
        }

        public void Rebuild() => Rebuild(new BoundsInt(Vector3Int.zero, levelData.Blocks.Size));

        public abstract void Rebuild(BoundsInt area);

        public virtual void Dispose()
        {

        }
    }

}
