const apiBaseUrl = localStorage.getItem("apiBaseUrl") || "http://localhost:5000";

const healthButton = document.querySelector("#healthButton");
const healthStatus = document.querySelector("#healthStatus");
const fileInput = document.querySelector("#fileInput");
const uploadButton = document.querySelector("#uploadButton");
const uploadResult = document.querySelector("#uploadResult");
const searchInput = document.querySelector("#searchInput");
const searchButton = document.querySelector("#searchButton");
const searchResult = document.querySelector("#searchResult");
const questionInput = document.querySelector("#questionInput");
const askButton = document.querySelector("#askButton");
const askResult = document.querySelector("#askResult");

function prettyPrint(target, value) {
  target.textContent = JSON.stringify(value, null, 2);
}

function printError(target, error) {
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

    prettyPrint(uploadResult, await parseResponse(response));
  } catch (error) {
    printError(uploadResult, error);
  }
});

searchButton.addEventListener("click", async () => {
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

    prettyPrint(searchResult, await parseResponse(response));
  } catch (error) {
    printError(searchResult, error);
  }
});

askButton.addEventListener("click", async () => {
  askResult.textContent = "Asking...";

  try {
    const response = await fetch(`${apiBaseUrl}/api/documents/ask`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        question: questionInput.value,
        topK: 3,
      }),
    });

    prettyPrint(askResult, await parseResponse(response));
  } catch (error) {
    printError(askResult, error);
  }
});
