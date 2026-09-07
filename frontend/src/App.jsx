import React, { useState } from "react";
import { searchDocuments, uploadDocument } from "./api";

const tabs = {
    upload: "upload",
    search: "search"
};

export default function App() {
    const [activeTab, setActiveTab] = useState(tabs.upload);

    const [file, setFile] = useState(null);
    const [uploadMessage, setUploadMessage] = useState("");
    const [uploadBusy, setUploadBusy] = useState(false);

    const [question, setQuestion] = useState("");
    const [searchBusy, setSearchBusy] = useState(false);
    const [answer, setAnswer] = useState("");
    const [sources, setSources] = useState([]);

    const onUpload = async (e) => {
        e.preventDefault();
        if (!file) return;

        setUploadBusy(true);
        setUploadMessage("");

        try {
            const result = await uploadDocument(file);
            setUploadMessage(`Success: ${result.message}. Chunks: ${result.chunks}`);
        } catch (err) {
            setUploadMessage(`Upload failed: ${err.message}`);
        } finally {
            setUploadBusy(false);
        }
    };

    const onSearch = async (e) => {
        e.preventDefault();
        if (!question.trim()) return;

        setSearchBusy(true);
        setAnswer("");
        setSources([]);

        try {
            const result = await searchDocuments(question);
            setAnswer(result.answer || "No answer returned.");
            setSources(result.sources || []);
        } catch (err) {
            setAnswer(`Search failed: ${err.message}`);
        } finally {
            setSearchBusy(false);
        }
    };

    return (
        <div className="layout">
            <aside className="sidebar">
                <h2>Lily RAG</h2>
                <button
                    className={activeTab === tabs.upload ? "nav active" : "nav"}
                    onClick={() => setActiveTab(tabs.upload)}
                >
                    Upload Documents
                </button>
                <button
                    className={activeTab === tabs.search ? "nav active" : "nav"}
                    onClick={() => setActiveTab(tabs.search)}
                >
                    Search Documents
                </button>
            </aside>

            <main className="content">
                {activeTab === tabs.upload && (
                    <section>
                        <h3>Upload Document</h3>
                        <form onSubmit={onUpload}>
                            <input
                                type="file"
                                accept=".pdf,.txt"
                                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                            />
                            <button type="submit" disabled={uploadBusy || !file}>
                                {uploadBusy ? "Uploading..." : "Upload"}
                            </button>
                        </form>
                        {uploadMessage && <p className="message">{uploadMessage}</p>}
                    </section>
                )}

                {activeTab === tabs.search && (
                    <section>
                        <h3>Search</h3>
                        <form onSubmit={onSearch}>
                            <textarea
                                rows={4}
                                placeholder="Ask a question from your indexed documents..."
                                value={question}
                                onChange={(e) => setQuestion(e.target.value)}
                            />
                            <button type="submit" disabled={searchBusy || !question.trim()}>
                                {searchBusy ? "Searching..." : "Search"}
                            </button>
                        </form>

                        {answer && (
                            <div className="card">
                                <h4>Answer</h4>
                                <p>{answer}</p>
                            </div>
                        )}

                        {sources.length > 0 && (
                            <div className="card">
                                <h4>Sources</h4>
                                <ul>
                                    {sources.map((s, i) => (
                                        <li key={i}>
                                            <strong>Score:</strong> {Number(s.score).toFixed(3)}
                                            <br />
                                            {s.content}
                                        </li>
                                    ))}
                                </ul>
                            </div>
                        )}
                    </section>
                )}
            </main>
        </div>
    );
}
