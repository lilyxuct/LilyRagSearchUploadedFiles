const API_BASE = (import.meta.env.VITE_API_BASE_URL || "").replace(/\/$/, "");

export async function uploadDocument(file) {
    const form = new FormData();
    form.append("file", file);

    const response = await fetch(`${API_BASE}/api/upload`, {
        method: "POST",
        body: form
    });

    if (!response.ok) {
        throw new Error(await response.text());
    }

    return response.json();
}

export async function searchDocuments(question) {
    const response = await fetch(`${API_BASE}/api/query`, {
        method: "POST",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ question })
    });

    if (!response.ok) {
        throw new Error(await response.text());
    }

    return response.json();
}