#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using KdTree;
using KdTree.Math;
using NINA.Core.Utility;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public class StarRegistry : IStarRegistry {
        private readonly List<StarField> registeredStarFields = new List<StarField>();
        private KdTree<float, RegisteredStar> globalStarRegistry = new KdTree<float, RegisteredStar>(2, new FloatMath(), AddDuplicateBehavior.Error);
        private readonly List<RegisteredStar> globalStarRegistryList = new List<RegisteredStar>();
        private double minAverageHFR = double.MaxValue;

        // TODO: Remove default value to force it to be provided
        public StarRegistry(float searchRadiusPixels = 100) {
            this.SearchRadiusPixels = searchRadiusPixels;
        }

        public float SearchRadiusPixels { get; private set; }

        public int StarFieldCount => this.registeredStarFields.Count;

        public override string ToString() {
            return $"{{{nameof(SearchRadiusPixels)}={SearchRadiusPixels.ToString()}, {nameof(StarFieldCount)}={StarFieldCount.ToString()}, {nameof(Count)}={Count.ToString()}";
        }

        public StarField GetStarField(int starFieldIndex) {
            return this.registeredStarFields[starFieldIndex];
        }

        public void AddStarField(int focuserPosition, StarDetectionResult starDetectionResult) {
            if (starDetectionResult.DetectedStars == 0) {
                return;
            }

            var addedStarField = new StarField(registeredStarFields.Count, focuserPosition, starDetectionResult);
            if (addedStarField.StarDetectionResult.AverageHFR < this.minAverageHFR) {
                if (registeredStarFields.Count > 0) {
                    Logger.Debug($"Resetting star field due to new field with smaller HFR {addedStarField.StarDetectionResult.AverageHFR}");
                    // KdTree Clear doesn't properly reset the count, so we initialize a new one from scratch
                    this.globalStarRegistry = new KdTree<float, RegisteredStar>(2, new FloatMath(), AddDuplicateBehavior.Error);
                    globalStarRegistryList.Clear();
                }
                AddStarFieldInternal(addedStarField);
                foreach (var previousStarField in registeredStarFields) {
                    AddStarFieldInternal(previousStarField);
                }
                this.minAverageHFR = addedStarField.StarDetectionResult.AverageHFR;
            } else {
                AddStarFieldInternal(addedStarField);
            }
            registeredStarFields.Add(addedStarField);
        }

        private void AddStarFieldInternal(StarField addedStarField) {
            var starDetectionResult = addedStarField.StarDetectionResult;
            var starFieldTree = new KdTree<float, DetectedStarIndex>(2, new FloatMath(), AddDuplicateBehavior.Error);
            foreach (var (star, starIndex) in starDetectionResult.StarList.Select((star, starIndex) => (star, starIndex))) {
                starFieldTree.Add(new[] { star.Position.X, star.Position.Y }, new DetectedStarIndex(starIndex, (HocusFocusDetectedStar)star));
            }

            var starIndexMap = new Dictionary<int, int>();
            var queue = new PriorityQueue<MatchingPair, double>(new DoubleMath());
            foreach (var starNode in starFieldTree) {
                var sourceStar = starNode.Value;
                var sourcePoint = starNode.Point;
                var globalNeighbors = globalStarRegistry.RadialSearch(sourcePoint, SearchRadiusPixels);
                foreach (var globalNeighbor in globalNeighbors) {
                    var distance = MathUtility.DotProduct(globalNeighbor.Point, sourcePoint);
                    queue.Enqueue(new MatchingPair() { SourceStar = sourceStar, GlobalRegistryStar = globalNeighbor.Value }, distance);
                }
            }

            var matchedGlobalStars = new bool[globalStarRegistry.Count];
            var matchedSourceStars = new bool[starFieldTree.Count];
            while (queue.Count > 0) {
                var nextCandidate = queue.Dequeue();
                if (matchedGlobalStars[nextCandidate.GlobalRegistryStar.Index] || matchedSourceStars[nextCandidate.SourceStar.Index]) {
                    continue;
                }

                nextCandidate.GlobalRegistryStar.MatchedStars.Add(new MatchedStar(addedStarField, nextCandidate.SourceStar.DetectedStar));
                matchedGlobalStars[nextCandidate.GlobalRegistryStar.Index] = true;
                matchedSourceStars[nextCandidate.SourceStar.Index] = true;
            }

            for (int j = 0; j < matchedSourceStars.Length; ++j) {
                if (matchedSourceStars[j]) {
                    continue;
                }

                // Now we've found a star that didn't match in the global registry. Add it to the registry for future matches
                // TODO: REMOVE THIS TRYCATCH after debugging
                var star = (HocusFocusDetectedStar)starDetectionResult.StarList[j];
                var nextGlobalIndex = globalStarRegistry.Count;
                var registeredStar = new RegisteredStar(nextGlobalIndex, new MatchedStar(addedStarField, star));
                try {
                    globalStarRegistry.Add(new[] { star.Position.X, star.Position.Y }, registeredStar);
                    globalStarRegistryList.Add(registeredStar);
                } catch (Exception e) {
                    Console.WriteLine(e);
                    throw;
                }
            }
        }

        #region IReadOnlyList Implementation

        public int Count => this.globalStarRegistry.Count;

        public RegisteredStar this[int index] => this.globalStarRegistryList[index];

        public IEnumerator<RegisteredStar> GetEnumerator() {
            return this.globalStarRegistryList.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return this.globalStarRegistryList.GetEnumerator();
        }

        #endregion

        #region Private helper classes

        private class DetectedStarIndex {

            public DetectedStarIndex(int index, HocusFocusDetectedStar star) {
                this.Index = index;
                this.DetectedStar = star;
            }

            public int Index { get; private set; }
            public HocusFocusDetectedStar DetectedStar { get; private set; }

            public override string ToString() {
                return $"{{{nameof(Index)}={Index.ToString()}, {nameof(DetectedStar)}={DetectedStar}}}";
            }
        }

        private class MatchingPair {
            public DetectedStarIndex SourceStar { get; set; }
            public RegisteredStar GlobalRegistryStar { get; set; }

            public override string ToString() {
                return $"{{{nameof(SourceStar)}={SourceStar}, {nameof(GlobalRegistryStar)}={GlobalRegistryStar}}}";
            }
        }

        #endregion
    }
}