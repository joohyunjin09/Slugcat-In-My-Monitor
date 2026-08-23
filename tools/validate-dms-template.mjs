import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, "..");
const defaultFixture = path.join(
  repositoryRoot,
  ".local-test-assets",
  "dms-template-1.6.6",
  "dms_template",
  "dressmyslugcat",
  "template",
);
const fixtureDirectory = path.resolve(process.argv[2] ?? defaultFixture);
const requiredAtlases = ["arm", "body", "face", "head", "hips", "legs", "tail"];

function fail(message) {
  throw new Error(message);
}

function parseJsonFile(filePath) {
  try {
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch (error) {
    fail(`${path.basename(filePath)} is not valid JSON: ${error.message}`);
  }
}

function readPngSize(filePath) {
  const data = fs.readFileSync(filePath);
  const signature = "89504e470d0a1a0a";
  if (data.length < 24 || data.subarray(0, 8).toString("hex") !== signature) {
    fail(`${path.basename(filePath)} is not a valid PNG file`);
  }
  if (data.subarray(12, 16).toString("ascii") !== "IHDR") {
    fail(`${path.basename(filePath)} has no PNG IHDR header`);
  }
  return {
    width: data.readUInt32BE(16),
    height: data.readUInt32BE(20),
  };
}

function assertString(value, field, fileName) {
  if (typeof value !== "string" || value.trim() === "") {
    fail(`${fileName} requires a non-empty ${field}`);
  }
}

function validateFrame(atlasName, frameName, descriptor, imageSize) {
  const frame = descriptor?.frame;
  if (!frame || ![frame.x, frame.y, frame.w, frame.h].every(Number.isInteger)) {
    fail(`${atlasName}: ${frameName} has an invalid frame rectangle`);
  }
  if (frame.x < 0 || frame.y < 0 || frame.w <= 0 || frame.h <= 0) {
    fail(`${atlasName}: ${frameName} has a non-positive frame rectangle`);
  }
  if (frame.x + frame.w > imageSize.width || frame.y + frame.h > imageSize.height) {
    fail(`${atlasName}: ${frameName} extends outside the PNG bounds`);
  }
}

function validateTemplate() {
  if (!fs.existsSync(fixtureDirectory) || !fs.statSync(fixtureDirectory).isDirectory()) {
    fail(`DMS template directory not found: ${fixtureDirectory}`);
  }

  const metadataPath = path.join(fixtureDirectory, "metadata.json");
  const metadata = parseJsonFile(metadataPath);
  assertString(metadata.id, "id", "metadata.json");
  assertString(metadata.name, "name", "metadata.json");
  assertString(metadata.author, "author", "metadata.json");

  for (const atlasName of requiredAtlases) {
    for (const extension of [".png", ".txt"]) {
      const expectedPath = path.join(fixtureDirectory, `${atlasName}${extension}`);
      if (!fs.existsSync(expectedPath)) {
        fail(`Required atlas file is missing: ${atlasName}${extension}`);
      }
    }
  }

  const atlasFiles = fs
    .readdirSync(fixtureDirectory)
    .filter((fileName) => fileName.endsWith(".txt"))
    .sort();

  const results = [];
  for (const atlasFile of atlasFiles) {
    const atlasName = path.basename(atlasFile, ".txt");
    const pngPath = path.join(fixtureDirectory, `${atlasName}.png`);
    if (!fs.existsSync(pngPath)) {
      fail(`${atlasFile} has no matching ${atlasName}.png`);
    }

    const atlas = parseJsonFile(path.join(fixtureDirectory, atlasFile));
    if (!atlas.frames || typeof atlas.frames !== "object") {
      fail(`${atlasFile} has no frames object`);
    }

    const imageSize = readPngSize(pngPath);
    const frames = Object.entries(atlas.frames);
    if (frames.length === 0) {
      fail(`${atlasFile} contains no frames`);
    }
    for (const [frameName, descriptor] of frames) {
      validateFrame(atlasName, frameName, descriptor, imageSize);
    }

    results.push({
      atlas: atlasName,
      frames: frames.length,
      image: `${imageSize.width}x${imageSize.height}`,
    });
  }

  return { metadata, atlases: results };
}

try {
  const result = validateTemplate();
  console.log(`DMS template valid: ${fixtureDirectory}`);
  console.table(result.atlases);
  console.log(`Skin metadata: ${result.metadata.id} (${result.metadata.name})`);
} catch (error) {
  console.error(`DMS template validation failed: ${error.message}`);
  process.exitCode = 1;
}
