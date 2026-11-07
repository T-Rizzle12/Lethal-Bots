using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LethalBots.AI.Pathfinding
{
    internal sealed class ActiveLinkTraversal
    {
        public LinkTraversalContext Context { get; }
        public INavMeshLinkInfo Handler { get; }
        public Coroutine? Coroutine { get; set; }

        public ActiveLinkTraversal(LinkTraversalContext context, INavMeshLinkInfo handler)
        {
            Context = context;
            Handler = handler;
        }
    }
}
