const apiBaseUrl = localStorage.getItem("apiBaseUrl") || "http://localhost:5000";

const healthButton = document.querySelector("#healthButton");
const healthStatus = document.querySelector("#healthStatus");
const fileInput = document.querySelector("#fileInput");
const uploadButton = document.querySelector("#uploadButton");
const uploadResult = document.querySelector("#uploadResult");
const refreshDocumentsButton = document.querySelector("#refreshDocumentsButton");
const documentsResult = document.querySelector("#documentsResult");
const searchInput = document.querySelector("#searchInput");
const searchButton = document.querySelector("#searchButton");
const searchResult = document.querySelector("#searchResult");
const questionInput = document.querySelector("#questionInput");
const askButton = document.querySelector("#askButton");
const answerResult = document.querySelector("#answerResult");
const sourceViewer = document.querySelector("#sourceViewer");

function printError(target, error) {
  target.classList.add("error-state");
  target.textContent = error instanceof Error ? error.message : String(error);
}

async function parseResponse(response) {
  const text = await response.text();
  const value = text ? JSON.parse(text) : null;

  if (!response.ok) {
    throw new Error(JSON.stringify(value, null, 2));
  }

  return value;
}

function clearState(target) {
  target.classList.remove("empty-state", "error-state");
  target.replaceChildren();
}

function showSource(source) {
  clearState(sourceViewer);

  const title = document.createElement("h3");
  title.textContent = source.fileName;

  const meta = document.createElement("p");
  meta.className = "source-meta";
  meta.textContent = `Chunk ${source.chunkIndex} · Score ${Number(source.score).toFixed(4)}`;

  const text = document.createElement("pre");
  text.textContent = source.text;

  sourceViewer.append(title, meta, text);
  sourceViewer.scrollIntoView({ behavior: "smooth", block: "start" });
}

function renderSources(target, sources) {
  clearState(target);

  if (!sources.length) {
    target.classList.add("empty-state");
    target.textContent = "No matching sources were returned.";
    return;
  }

  sources.forEach((source) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "source-item";

    const title = document.createElement("strong");
    title.textContent = source.fileName;

    const meta = document.createElement("span");
    meta.textContent = `Chunk ${source.chunkIndex} · ${Number(source.score).toFixed(4)}`;

    const preview = document.createElement("span");
    preview.className = "source-preview";
    preview.textContent = source.text.length > 160 ? `${source.text.slice(0, 160)}...` : source.text;

    button.append(title, meta, preview);
    button.addEventListener("click", () => showSource(source));
    target.append(button);
  });
}

function renderDocuments(documents) {
  clearState(documentsResult);

  if (!documents.length) {
    documentsResult.classList.add("empty-state");
    documentsResult.textContent = "No documents have been uploaded yet.";
    return;
  }

  documents.forEach((documentItem) => {
    const item = document.createElement("article");
    item.className = "document-item";

    const title = document.createElement("strong");
    title.textContent = documentItem.fileName;

    const status = document.createElement("span");
    status.className = "document-status";
    status.textContent = documentItem.status;

    const details = document.createElement("span");
    const size = Number(documentItem.sizeInBytes || 0).toLocaleString();
    details.textContent = `${documentItem.contentType || "unknown"} · ${size} bytes`;

    item.append(title, status, details);
    documentsResult.append(item);
  });
}

async function loadDocuments() {
  documentsResult.className = "document-list empty-state";
  documentsResult.textContent = "Loading documents...";

  try {
    const response = await fetch(`${apiBaseUrl}/api/documents`);
    renderDocuments(await parseResponse(response));
  } catch (error) {
    printError(documentsResult, error);
  }
}

healthButton.addEventListener("click", async () => {
  healthStatus.textContent = "Checking...";

  try {
    const response = await fetch(`${apiBaseUrl}/health`);
    const value = await parseResponse(response);
    healthStatus.textContent = `${value.service}: ${value.status}`;
  } catch (error) {
    healthStatus.textContent = "API is not reachable";
  }
});

refreshDocumentsButton.addEventListener("click", loadDocuments);

uploadButton.addEventListener("click", async () => {
  const file = fileInput.files[0];

  if (!file) {
    uploadResult.textContent = "Choose a file first.";
    return;
  }

  const formData = new FormData();
  formData.append("file", file);
  uploadResult.textContent = "Uploading...";

  try {
    const response = await fetch(`${apiBaseUrl}/api/documents/upload`, {
      method: "POST",
      body: formData,
    });

    uploadResult.textContent = JSON.stringify(await parseResponse(response), null, 2);
    await loadDocuments();
  } catch (error) {
    printError(uploadResult, error);
  }
});

searchButton.addEventListener("click", async () => {
  searchResult.className = "source-list empty-state";
  searchResult.textContent = "Searching...";

  try {
    const response = await fetch(`${apiBaseUrl}/api/documents/search`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        query: searchInput.value,
        topK: 3,
      }),
    });

    const value = await parseResponse(response);
    renderSources(searchResult, value.matches || []);
  } catch (error) {
    printError(searchResult, error);
  }
});

askButton.addEventListener("click", async () => {
  answerResult.className = "answer-panel empty-state";
  answerResult.textContent = "Asking...";

  try {
    const response = await fetch(`${apiBaseUrl}/api/documents/ask`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        question: questionInput.value,
        topK: 3,
      }),
    });

    const value = await parseResponse(response);
    clearState(answerResult);

    const answer = document.createElement("p");
    answer.className = "answer-text";
    answer.textContent = value.answer;

    const sources = document.createElement("div");
    sources.className = "source-list";
    renderSources(sources, value.sources || []);

    answerResult.append(answer, sources);
  } catch (error) {
    printError(answerResult, error);
  }
});

loadDocuments();
