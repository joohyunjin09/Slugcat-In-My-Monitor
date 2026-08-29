from pathlib import Path


def read(path):
    return Path(path).read_text(encoding="utf-8")


def write(path, text):
    Path(path).write_text(text, encoding="utf-8", newline="")


overlay_path = "src/RainWorldDesktopPet/UI/LayeredOverlayWindow.cs"
text = read(overlay_path)
old = '''                    circles[circleCount++] = new MouseHookHitCircle(\n                        loop.ToRenderedScreen(food.Chunk.Position),\n                        loop.ToRenderedScreenLength(food.VisualReach + 5.0));\n'''
new = '''                    circles[circleCount++] = new MouseHookHitCircle(\n                        ResolveWorldFoodHitCenter(food.Chunk.Position),\n                        ResolveWorldFoodHitRadius(food.VisualReach + 5.0));\n'''
if text.count(old) != 1:
    raise SystemExit("food mouse-hit transform block mismatch")
text = text.replace(old, new, 1)

marker = "        private void PublishMouseHitSnapshot()\n"
helpers = '''        internal static Vec2 ResolveWorldFoodHitCenter(Vec2 simulationPosition)\n        {\n            return DesktopWorldTransform.ToDesktop(simulationPosition);\n        }\n\n        internal static double ResolveWorldFoodHitRadius(double simulationRadius)\n        {\n            return DesktopWorldTransform.ToDesktopLength(simulationRadius);\n        }\n\n'''
if text.count(marker) != 1:
    raise SystemExit("PublishMouseHitSnapshot marker mismatch")
text = text.replace(marker, helpers + marker, 1)
write(overlay_path, text)


tests_path = "tests/RainWorldDesktopPet.Tests/Program.cs"
text = read(tests_path)
registration = '''            Run("Mouse hook hit snapshots preserve click-through and topmost order",\n                MouseHookHitSnapshotsPreserveInputRules);\n'''
added_registration = registration + '''            Run("World food mouse hit circles ignore Slugcat visual size",\n                WorldFoodMouseHitCirclesIgnoreSlugcatVisualSize);\n'''
if text.count(registration) != 1:
    raise SystemExit("mouse hook test registration mismatch")
text = text.replace(registration, added_registration, 1)

method_marker = "        private static void MouseHookHitSnapshotsPreserveInputRules()\n"
new_test = r'''        private static void WorldFoodMouseHitCirclesIgnoreSlugcatVisualSize()
        {
            Vec2 foodPosition = new Vec2(180.0, 92.0);
            double foodReach = 13.0;
            Vec2 expectedCenter = DesktopWorldTransform.ToDesktop(foodPosition);
            double expectedRadius = DesktopWorldTransform.ToDesktopLength(foodReach);

            Vec2 actualCenter = LayeredOverlayWindow.ResolveWorldFoodHitCenter(
                foodPosition);
            double actualRadius = LayeredOverlayWindow.ResolveWorldFoodHitRadius(
                foodReach);
            Near(0.0, Vec2.Distance(expectedCenter, actualCenter), 0.000001,
                "world food mouse hit center matches the rendered desktop position");
            Near(expectedRadius, actualRadius, 0.000001,
                "world food mouse hit radius matches the rendered desktop size");

            Vec2 characterCenter = new Vec2(70.0, 55.0);
            Vec2 smallCharacterMapped = DesktopWorldTransform.ToDesktop(
                characterCenter + (foodPosition - characterCenter) *
                    SlugcatSizeSettings.SmallMultiplier);
            Vec2 normalCharacterMapped = DesktopWorldTransform.ToDesktop(
                characterCenter + (foodPosition - characterCenter) *
                    SlugcatSizeSettings.NormalMultiplier);
            True(Vec2.Distance(actualCenter, smallCharacterMapped) > 1.0,
                "Small Slugcat character scaling must not move the world-food hit circle");
            True(Vec2.Distance(actualCenter, normalCharacterMapped) > 1.0,
                "Normal Slugcat character scaling must not move the world-food hit circle");
        }

'''
if text.count(method_marker) != 1:
    raise SystemExit("mouse hook test method marker mismatch")
text = text.replace(method_marker, new_test + method_marker, 1)
write(tests_path, text)
