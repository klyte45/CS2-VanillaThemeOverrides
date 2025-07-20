using BridgeWE;
using Colossal.Entities;
using Game.Buildings;
using Game.Common;
using Game.Net;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using VanillaThemeOverride.Systems;

namespace VanillaThemeOverride.Functions
{
    public static class StreetPlateFn
    {
        public struct StreetPlateCacheData : IComponentData
        {
            public int thisMinNumber;
            public int thisMaxNumber;
            public int otherMinNumber;
            public int otherMaxNumber;
            public bool thisNodeIsAtEnd;
            public bool otherNodeIsAtEnd;
            public float thisRoadAngle;
            public float otherRoadAngle;
            public Entity thisEdge;
            public Entity otherEdge;
            public Colossal.Hash128 thisVersion;
            public Colossal.Hash128 otherVersion;

            public readonly bool IsUpToDate(Entity edge, Colossal.Hash128 version)
                => (edge == thisEdge && version == thisVersion) || (edge == otherEdge && version == otherVersion);
        }

        public static string GetNumberRange(Entity reference, Dictionary<string, string> vars)
            => RunWithCache(reference, vars, "~", GenerateNumberText);


        public static float3 GetSignDirectionAngle(Entity reference, Dictionary<string, string> vars)
            => RunWithCache(
                reference,
                vars,
                default,
                (_, data, cachedData) => new float3(0, data.RefEdge == cachedData.thisEdge ? cachedData.thisRoadAngle : cachedData.otherRoadAngle, 0)
                );



        private delegate T StreetPlateCacheResultGetter<T>(
          Dictionary<string, string> vars,
          (Entity RefEdge, ushort AzimuthDirection16bits, float3 CenterPoint, float3 RefPoint, Colossal.Hash128 VersionIdentifier) data,
          StreetPlateCacheData cachedData
            );

        private static T RunWithCache<T>(Entity reference, Dictionary<string, string> vars, T defaultValue, StreetPlateCacheResultGetter<T> resultGetterFn)
        {
            var data = WERoadFnBridge.GetFromPropByTargetVar(reference, vars);
            if (data.RefEdge == Entity.Null)
            {
                return defaultValue;
            }
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            if (em.TryGetComponent<StreetPlateCacheData>(reference, out var cachedData))
            {
                if (cachedData.IsUpToDate(data.RefEdge, data.VersionIdentifier))
                {
                    return resultGetterFn(vars, data, cachedData);
                }
                else
                {
                    EdgeExtraDataUpdater.EnqueueToRun((ecb) =>
                    {
                        ecb.RemoveComponent<StreetPlateCacheData>(reference);
                    });
                }
            }

            cachedData = UpdateCache(reference, em);
            if (cachedData.thisEdge == Entity.Null)
            {
                return defaultValue;
            }

            return resultGetterFn(vars, data, cachedData);
        }

        private static string GenerateNumberText(Dictionary<string, string> vars, (Entity RefEdge, ushort AzimuthDirection16bits, float3 CenterPoint, float3 RefPoint, Colossal.Hash128 VersionIdentifier) data, StreetPlateCacheData cachedData)
        {
            var inverted = vars.ContainsKey("inverted") == (data.RefEdge == cachedData.thisEdge ? cachedData.thisNodeIsAtEnd : cachedData.otherNodeIsAtEnd);
            return data.RefEdge == cachedData.thisEdge
                ? inverted ? $"{cachedData.thisMaxNumber} ~ {cachedData.thisMinNumber}" : $"{cachedData.thisMinNumber} ~ {cachedData.thisMaxNumber}"
                : inverted ? $"{cachedData.otherMaxNumber} ~ {cachedData.otherMinNumber}" : $"{cachedData.otherMinNumber} ~ {cachedData.otherMaxNumber}";
        }

        private static StreetPlateCacheData UpdateCache(Entity reference, EntityManager em)
        {
            var dataThis = WERoadFnBridge.GetRoadOwnSegmentForProp(reference);
            var dataOther = WERoadFnBridge.GetRoadSideSegmentForProp(reference);

            MapNumberRange(reference, dataThis, em, out bool thisNodeIsAtEnd, out int thisMinNumber, out int thisMaxNumber, out bool successThis);
            MapNumberRange(reference, dataOther, em, out bool otherNodeIsAtEnd, out int otherMinNumber, out int otherMaxNumber, out bool successOther);
            if (!successOther || !successThis)
            {
                return default;
            }

            var thisRoadAngle = dataThis.AzimuthDirection16bits / 65536f * 360;
            var otherRoadAngle = dataOther.AzimuthDirection16bits / 65536f * 360;

            StreetPlateCacheData cacheData = new()
            {
                thisMinNumber = thisMinNumber,
                thisMaxNumber = thisMaxNumber,
                thisEdge = dataThis.RefEdge,
                otherEdge = dataOther.RefEdge,
                thisVersion = dataThis.VersionIdentifier,
                otherVersion = dataOther.VersionIdentifier,
                otherMaxNumber = otherMaxNumber,
                otherMinNumber = otherMinNumber,
                thisRoadAngle = 90,
                otherRoadAngle = 90 - thisRoadAngle + otherRoadAngle,
                thisNodeIsAtEnd = thisNodeIsAtEnd,
                otherNodeIsAtEnd = otherNodeIsAtEnd

            };
            EdgeExtraDataUpdater.EnqueueToRun((ecb) =>
            {
                ecb.AddComponent(reference, cacheData);
            });
            return cacheData;
        }

        private static void MapNumberRange(Entity reference,
            (Entity RefEdge, ushort AzimuthDirection16bits, Unity.Mathematics.float3 CenterPoint, Unity.Mathematics.float3 RefPoint, Colossal.Hash128 VersionIdentifier) data,
            EntityManager em, out bool nodeIsAtEnd, out int minNumber, out int maxNumber, out bool success)
        {
            var nextEdge = data.RefEdge;
            var refNode = em.GetComponentData<Owner>(reference).m_Owner;
            minNumber = int.MaxValue;
            maxNumber = int.MinValue;
            success = true;
            while (em.TryGetComponent<Edge>(nextEdge, out var edgeNodes))
            {
                if (!em.TryGetComponent<EdgeExtraDataUpdater.EdgeNodeInformation>(nextEdge, out var edgeNodeInfo))
                {
                    EdgeExtraDataUpdater.UpdateEdgeData(nextEdge);
                    success = false;
                }
                else
                {
                    if (edgeNodeInfo.minNumber < minNumber)
                    {
                        minNumber = edgeNodeInfo.minNumber;
                    }
                    if (edgeNodeInfo.maxNumber > maxNumber)
                    {
                        maxNumber = edgeNodeInfo.maxNumber;
                    }
                }
                var otherNode = edgeNodes.m_Start == refNode
                    ? edgeNodes.m_End
                    : edgeNodes.m_Start;

                if (em.TryGetBuffer<ConnectedEdge>(otherNode, true, out var edgesConnected)
                    && edgesConnected.Length == 2
                    && em.TryGetComponent<Aggregated>(edgesConnected[0].m_Edge, out var agg0)
                    && em.TryGetComponent<Aggregated>(edgesConnected[1].m_Edge, out var agg1)
                    && agg0.m_Aggregate == agg1.m_Aggregate)
                {
                    nextEdge = edgesConnected[0].m_Edge == nextEdge
                        ? edgesConnected[1].m_Edge
                        : edgesConnected[0].m_Edge;
                    refNode = otherNode;
                }
                else
                {
                    break;
                }
            }

            BuildingUtils.GetAddress(em, reference, data.RefEdge, 0.05f, out _, out var propNumber);
            var numberSide = propNumber & 1;
            maxNumber &= ~1;
            minNumber &= ~1;
            maxNumber |= numberSide;
            minNumber |= numberSide;

            nodeIsAtEnd = math.abs(propNumber - maxNumber) < math.abs(propNumber - minNumber);
        }
    }
}
