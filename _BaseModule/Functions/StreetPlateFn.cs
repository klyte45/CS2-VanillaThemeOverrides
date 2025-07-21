using Belzont.Utils;
using BridgeWE;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VanillaThemeOverride.Systems;
using Debug = UnityEngine.Debug;

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
            public bool thisNodeIsMinNumber;
            public bool otherNodeIsMinNumber;
            public float thisRoadAngle;
            public float otherRoadAngle;
            public Entity thisEdge;
            public Entity otherEdge;
            public Colossal.Hash128 thisVersion;
            public Colossal.Hash128 otherVersion;
            public float3 offsetPosition;

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
        public static float3 GetSignOffsetPosition(Entity reference, Dictionary<string, string> vars)
            => RunWithCache(reference, vars, default, (_, data, cachedData) => cachedData.offsetPosition);



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
            var inverted = vars.ContainsKey("inverted") != (data.RefEdge == cachedData.thisEdge ? cachedData.thisNodeIsMinNumber : cachedData.otherNodeIsMinNumber);
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
            em.TryGetComponent<Game.Objects.Transform>(reference, out var transform);

            em.TryGetComponent<Owner>(reference, out var owner);
            em.TryGetComponent<Node>(owner.m_Owner, out var nodeData);
            em.TryGetBuffer<Game.Net.SubLane>(owner.m_Owner, true, out var lanes);

            var propPosition = float3.zero;
            var nearestDistanceAngle = float.MaxValue;

            em.TryGetComponent<Curve>(dataThis.RefEdge, out var curveThis);
            var midCurve = MathUtils.Position(curveThis.m_Bezier, .5f);
            var nodeCenterCurve = new Bezier4x3(dataThis.CenterPoint, dataThis.CenterPoint, midCurve, midCurve);
            var isOnOddSideMainProp = IsOddSide(transform.m_Position, nodeCenterCurve, .5f);
            float targetAngle = (thisRoadAngle + otherRoadAngle + ((thisRoadAngle > otherRoadAngle) == isOnOddSideMainProp ? 360 : 0)) / 2 % 360;


            for (int i = 0; i < lanes.Length; i++)
            {
                if ((lanes[i].m_PathMethods & Game.Pathfind.PathMethod.Pedestrian) != 0 && em.TryGetComponent<Curve>(lanes[i].m_SubLane, out var laneCurve))
                {
                    MathUtils.Distance(laneCurve.m_Bezier.xz, dataThis.CenterPoint.xz, out float pos);
                    var angle = dataThis.CenterPoint.xz.GetAngleToPoint(MathUtils.Position(laneCurve.m_Bezier, pos).xz);
                    var diffAngle = math.abs(targetAngle - angle);
                    if (diffAngle > 180) diffAngle = 360 - diffAngle;

                    if (diffAngle < nearestDistanceAngle)
                    {
                        nearestDistanceAngle = diffAngle;
                        propPosition = MathUtils.Position(laneCurve.m_Bezier, pos);
                    }

                }
            }

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
                thisRoadAngle = thisRoadAngle - ((Quaternion)transform.m_Rotation).eulerAngles.y,
                otherRoadAngle = otherRoadAngle - ((Quaternion)transform.m_Rotation).eulerAngles.y,
                thisNodeIsMinNumber = thisNodeIsAtEnd,
                otherNodeIsMinNumber = otherNodeIsAtEnd,
                offsetPosition = new float3(((float3)(Matrix4x4.Rotate(transform.m_Rotation).inverse.MultiplyPoint(propPosition - transform.m_Position))).xz, 0).xzy

            };
            EdgeExtraDataUpdater.EnqueueToRun((ecb) =>
            {
                ecb.AddComponent(reference, cacheData);
            });
            return cacheData;
        }

        private static bool IsOddSide(float3 refPos, Bezier4x3 curve, float curvePos, bool inverted = false)
        {
            float2 x2 = refPos.xz - MathUtils.Position(curve, curvePos).xz;
            float2 y2 = MathUtils.Right(MathUtils.Tangent(curve, curvePos).xz);
            return (math.dot(x2, y2) > 0f) != inverted;
        }

        private static void MapNumberRange(Entity reference,
            (Entity RefEdge, ushort AzimuthDirection16bits, Unity.Mathematics.float3 CenterPoint, Unity.Mathematics.float3 RefPoint, Colossal.Hash128 VersionIdentifier) data,
            EntityManager em, out bool nodeIsMinNumber, out int minNumber, out int maxNumber, out bool success)
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

            BuildingUtils.GetAddress(em, reference, data.RefEdge, 0f, out _, out var startSegmentNumber);
            var numberSide = startSegmentNumber & 1;
            maxNumber &= ~1;
            minNumber &= ~1;
            maxNumber |= numberSide;
            minNumber |= numberSide;


            BuildingUtils.GetAddress(em, reference, data.RefEdge, 1f, out _, out var endSegmentNumber);

            var edgeCurve = em.GetComponentData<Curve>(data.RefEdge);

            nodeIsMinNumber = (Vector3)edgeCurve.m_Bezier.a == (Vector3)data.CenterPoint == (startSegmentNumber < endSegmentNumber);
        }

        private static readonly Dictionary<string, string> AbbreviatedNamesCache = [];
        private static readonly List<(Regex Matching, string Replacement)> LoadedAbbreviations = [];
        private static readonly string ABBREVIATIONS_FILE_LOCATION = Path.Combine(Application.persistentDataPath, "ModsData", "Klyte45Mods", "VanillaThemeOverriding", "abbreviations.txt");
        private static readonly FileSystemWatcher AbbreviationsFileWatcher;

        static StreetPlateFn()
        {
            if (!File.Exists(ABBREVIATIONS_FILE_LOCATION))
            {
                EnsureFolderCreation(Path.GetDirectoryName(ABBREVIATIONS_FILE_LOCATION));
                File.WriteAllText(ABBREVIATIONS_FILE_LOCATION, """
                    # Example:
                    # Street = St
                    # Remove the initial '#' to make the line effective!
                    # The entries accepts C# Regex features. See full docs for reference: 
                    # https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference
                    # Plus, starting an line with (?i) will make the regex case insensitive. Use backslash (\) to escape the "=" used inside the regexes
                    # Abbreviations files from Write Everywhere/Write the Signs for Cities Skylines 1 may work too:
                    # https://github.com/klyte45/WriteTheSignsFiles/tree/master/abbreviations
                    """);
            }
            AbbreviationsFileWatcher = new(Path.GetDirectoryName(ABBREVIATIONS_FILE_LOCATION), Path.GetFileName(ABBREVIATIONS_FILE_LOCATION))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            LoadAbbreviations();
            AbbreviationsFileWatcher.Changed += (sender, args) => LoadAbbreviations();
            AbbreviationsFileWatcher.EnableRaisingEvents = true;
        }

        private static FileInfo EnsureFolderCreation(string folderName)
        {
            if (File.Exists(folderName) && (File.GetAttributes(folderName) & FileAttributes.Directory) != FileAttributes.Directory)
            {
                File.Delete(folderName);
            }
            if (!Directory.Exists(folderName))
            {
                Directory.CreateDirectory(folderName);
            }
            return new FileInfo(folderName);
        }

        private static void LoadAbbreviations()
        {
            LoadedAbbreviations.Clear();
            try
            {
                foreach (var line in File.ReadAllLines(ABBREVIATIONS_FILE_LOCATION))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;
                    var parts = line.Replace("\\=", "≠").Split('=', 2);
                    if (parts.Length == 2)
                    {
                        try
                        {
                            var effectiveRegex = parts[0].Replace("≠", "=").Trim();
                            var regexOptions = RegexOptions.CultureInvariant;
                            if (effectiveRegex.StartsWith("(?i)"))
                            {
                                regexOptions |= RegexOptions.IgnoreCase;
                                effectiveRegex = effectiveRegex[4..];
                            }
                            if (Regex.IsMatch(effectiveRegex, @"^[\p{L}\w 0-9]*$"))
                            {
                                effectiveRegex = $@"\b{effectiveRegex}\b";
                            }

                            LoadedAbbreviations.Add((new Regex(effectiveRegex, regexOptions, TimeSpan.FromMilliseconds(100)), parts[1].Replace("≠", "=").Trim()));
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Error processing line '{line}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading abbreviations: {ex.Message}");
            }
            AbbreviatedNamesCache.Clear();
        }

        public static string ApplyAbbreviations(string text)
        {
            if (AbbreviatedNamesCache.TryGetValue(text, out var cachedValue))
            {
                return cachedValue;
            }
            var replacementText = text;
            foreach (var kvp in LoadedAbbreviations)
            {
                var pattern = kvp.Matching;
                replacementText = kvp.Matching.Replace(
                    replacementText,
                    kvp.Replacement
                ).Trim();
            }
            return AbbreviatedNamesCache[text] = replacementText;
        }
    }
}
