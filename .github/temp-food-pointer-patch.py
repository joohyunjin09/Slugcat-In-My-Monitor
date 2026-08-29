from pathlib import Path


def read(path):
    return Path(path).read_text(encoding="utf-8")


def write(path, text):
    Path(path).write_text(text, encoding="utf-8", newline="")


game_path = "src/RainWorldDesktopPet/Core/GameLoop.cs"
text = read(game_path)
old = '''            mouse.Sample(elapsed);\n            Vec2 visualPointer = PointerToSimulation(mouse.Position);\n            Vec2 visualPointerVelocity = mouse.Velocity / VisualSizeMultiplier;\n            Foods.MoveDraggedFood(visualPointer);\n'''
new = '''            mouse.Sample(elapsed);\n            Vec2 worldPointer;\n            Vec2 visualPointer;\n            ResolvePointerSpaces(mouse.Position, Slugcat.Center,\n                VisualSizeMultiplier, out worldPointer, out visualPointer);\n            Vec2 visualPointerVelocity = mouse.Velocity / VisualSizeMultiplier;\n            // Food is a desktop/world object while it is being positioned.\n            // Never feed it the character-local pointer that is scaled around\n            // Slugcat.Center for Small/Normal visual sizes.\n            Foods.MoveDraggedFood(worldPointer);\n'''
if text.count(old) != 1:
    raise SystemExit("Advance pointer block mismatch")
text = text.replace(old, new, 1)

old = '''        public bool HitTest(Vec2 screenPoint)\n        {\n            Vec2 simulationPoint = PointerToSimulation(\n                DesktopWorldTransform.ToSimulation(screenPoint));\n            return Foods.HitTest(simulationPoint) ||\n                Slugcat.HitTest(simulationPoint) ||\n                Vec2.Distance(simulationPoint, Graphics.Head.Position) < 17.0;\n        }\n\n        public bool BeginGrab(Vec2 screenPoint)\n        {\n            Vec2 simulationPoint = PointerToSimulation(\n                DesktopWorldTransform.ToSimulation(screenPoint));\n            if (Foods.TryBeginDrag(simulationPoint)) return true;\n            if (Slugcat.Grab(simulationPoint)) return true;\n            if (Vec2.Distance(simulationPoint, Graphics.Head.Position) < 17.0)\n            {\n                return Slugcat.Grab(Slugcat.BodyChunks[0].Position);\n            }\n            return false;\n        }\n\n        public void EndGrab()\n        {\n            Vec2 pointerVelocity = mouse.Velocity / VisualSizeMultiplier;\n            if (Foods.EndDrag(Vec2.ClampMagnitude(pointerVelocity /\n                SimulationConstants.LogicTicksPerSecond, 25.0))) return;\n            Slugcat.Release(pointerVelocity);\n        }\n'''
new = '''        public bool HitTest(Vec2 screenPoint)\n        {\n            Vec2 worldPoint;\n            Vec2 simulationPoint;\n            ResolvePointerSpaces(DesktopWorldTransform.ToSimulation(screenPoint),\n                Slugcat.Center, VisualSizeMultiplier, out worldPoint,\n                out simulationPoint);\n            return Foods.HitTest(worldPoint) ||\n                Slugcat.HitTest(simulationPoint) ||\n                Vec2.Distance(simulationPoint, Graphics.Head.Position) < 17.0;\n        }\n\n        public bool BeginGrab(Vec2 screenPoint)\n        {\n            Vec2 worldPoint;\n            Vec2 simulationPoint;\n            ResolvePointerSpaces(DesktopWorldTransform.ToSimulation(screenPoint),\n                Slugcat.Center, VisualSizeMultiplier, out worldPoint,\n                out simulationPoint);\n            if (Foods.TryBeginDrag(worldPoint)) return true;\n            if (Slugcat.Grab(simulationPoint)) return true;\n            if (Vec2.Distance(simulationPoint, Graphics.Head.Position) < 17.0)\n            {\n                return Slugcat.Grab(Slugcat.BodyChunks[0].Position);\n            }\n            return false;\n        }\n\n        public void EndGrab()\n        {\n            // Food drag velocity is already in unscaled desktop-world simulation\n            // units. Character dragging alone needs the inverse visual-size scale.\n            if (Foods.IsDragging)\n            {\n                Foods.EndDrag(Vec2.ClampMagnitude(mouse.Velocity /\n                    SimulationConstants.LogicTicksPerSecond, 25.0));\n                return;\n            }\n            Slugcat.Release(mouse.Velocity / VisualSizeMultiplier);\n        }\n'''
if text.count(old) != 1:
    raise SystemExit("HitTest/BeginGrab/EndGrab block mismatch")
text = text.replace(old, new, 1)

old = '''        private Vec2 PointerToSimulation(Vec2 normalSimulationPoint)\n        {\n            return Slugcat.Center + (normalSimulationPoint - Slugcat.Center) /\n                VisualSizeMultiplier;\n        }\n'''
new = '''        private Vec2 PointerToSimulation(Vec2 normalSimulationPoint)\n        {\n            Vec2 worldPointer;\n            Vec2 characterPointer;\n            ResolvePointerSpaces(normalSimulationPoint, Slugcat.Center,\n                VisualSizeMultiplier, out worldPointer, out characterPointer);\n            return characterPointer;\n        }\n\n        internal static void ResolvePointerSpaces(Vec2 normalSimulationPoint,\n            Vec2 characterCenter, double visualSizeMultiplier,\n            out Vec2 worldPointer, out Vec2 characterPointer)\n        {\n            if (visualSizeMultiplier <= 0.0 ||\n                double.IsNaN(visualSizeMultiplier) ||\n                double.IsInfinity(visualSizeMultiplier))\n                throw new ArgumentOutOfRangeException("visualSizeMultiplier");\n            worldPointer = normalSimulationPoint;\n            characterPointer = characterCenter +\n                (normalSimulationPoint - characterCenter) / visualSizeMultiplier;\n        }\n'''
if text.count(old) != 1:
    raise SystemExit("PointerToSimulation block mismatch")
text = text.replace(old, new, 1)
write(game_path, text)


tests_path = "tests/RainWorldDesktopPet.Tests/Program.cs"
text = read(tests_path)
registration = '''            Run("Blue Fruit and Eggbug Egg can be repositioned with the mouse",\n                FoodItemsSupportMouseDragging);\n'''
added = registration + '''            Run("Food placement pointer stays in world space for Small and Normal",\n                FoodPlacementPointerIgnoresSlugcatVisualScale);\n'''
if text.count(registration) != 1:
    raise SystemExit("Food drag test registration mismatch")
text = text.replace(registration, added, 1)

marker = "        private static void FoodFallbackRendersWithoutAtlas()\n"
idx = text.find(marker)
if idx < 0:
    raise SystemExit("Food fallback test marker missing")
new_test = r'''        private static void FoodPlacementPointerIgnoresSlugcatVisualScale()
        {
            Vec2 cursor = new Vec2(410.0, 215.0);
            Vec2 slugcatCenter = new Vec2(120.0, 95.0);
            SlugcatSize[] sizes = { SlugcatSize.Small, SlugcatSize.Normal };

            for (int i = 0; i < sizes.Length; i++)
            {
                double multiplier = SlugcatSizeSettings.Multiplier(sizes[i]);
                Vec2 worldPointer;
                Vec2 characterPointer;
                GameLoop.ResolvePointerSpaces(cursor, slugcatCenter, multiplier,
                    out worldPointer, out characterPointer);

                Near(0.0, Vec2.Distance(cursor, worldPointer), 0.000001,
                    sizes[i] + " food placement keeps the raw desktop pointer");
                True(Vec2.Distance(worldPointer, characterPointer) > 1.0,
                    sizes[i] + " character pointer remains separately size-adjusted");

                DesktopFoodManager manager = new DesktopFoodManager(9300 + i);
                True(manager.TryAddDangleFruit(cursor),
                    sizes[i] + " test food can be created at cursor position");
                True(manager.TryBeginDrag(cursor),
                    sizes[i] + " test food can enter mouse drag");
                manager.MoveDraggedFood(worldPointer);
                Near(0.0, Vec2.Distance(cursor,
                    manager.DraggedFood.Chunk.Position), 0.000001,
                    sizes[i] + " dragged food stays under the real cursor");
                manager.EndDrag(Vec2.Zero);
            }
        }

'''
text = text[:idx] + new_test + text[idx:]
write(tests_path, text)
