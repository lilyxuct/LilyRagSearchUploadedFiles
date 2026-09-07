import React, { useReducer } from "react";
import { searchDocuments, uploadDocument } from "./api";

const tabs = {
    upload: "upload",
    search: "search"
};

const initialState = {
    activeTab: tabs.upload,
    file: null,
    uploadMessage: "",
    uploadBusy: false,
    question: "",
    searchBusy: false,
    answer: "",
    sources: []
};

function reducer(state, action) {
    switch (action.type) {
        case "SET_TAB":
            return { ...state, activeTab: action.payload };

        case "SET_FILE":
            return { ...state, file: action.payload };

        case "UPLOAD_START":
            return { ...state, uploadBusy: true, uploadMessage: "" };

        case "UPLOAD_SUCCESS":
            return {
                ...state,
                uploadBusy: false,
                uploadMessage: `Success: ${action.payload.message}. Chunks: ${action.payload.chunks}`
            };

        case "UPLOAD_ERROR":
            return {
                ...state,
                uploadBusy: false,
                uploadMessage: `Upload failed: ${action.payload}`
            };

        case "SET_QUESTION":
            return { ...state, question: action.payload };

        case "SEARCH_START":
            return { ...state, searchBusy: true, answer: "", sources: [] };

        case "SEARCH_SUCCESS":
            return {
                ...state,
                searchBusy: false,
                answer: action.payload.answer || "No answer returned.",
                sources: action.payload.sources || []
            };

        case "SEARCH_ERROR":
            return {
                ...state,
                searchBusy: false,
                answer: `Search failed: ${action.payload}`
            };

        default:
            return state;
    }
}

export default function App() {
    const [state, dispatch] = useReducer(reducer, initialState);

    const onUpload = async (e) => {
        e.preventDefault();
        if (!state.file) return;

        dispatch({ type: "UPLOAD_START" });

        try {
            const result = await uploadDocument(state.file);
            dispatch({ type: "UPLOAD_SUCCESS", payload: result });
        } catch (err) {
            dispatch({ type: "UPLOAD_ERROR", payload: err.message });
        }
    };

    const onSearch = async (e) => {
        e.preventDefault();
        if (!state.question.trim()) return;

        dispatch({ type: "SEARCH_START" });

        try {
            const result = await searchDocuments(state.question);
            dispatch({ type: "SEARCH_SUCCESS", payload: result });
        } catch (err) {
            dispatch({ type: "SEARCH_ERROR", payload: err.message });
        }
    };

    return (
        <div className="layout">
            <aside className="sidebar">
                <h2>Lily RAG</h2>
                <button
                    className={state.activeTab === tabs.upload ? "nav active" : "nav"}
                    onClick={() => dispatch({ type: "SET_TAB", payload: tabs.upload })}
                >
                    Upload Documents
                </button>
                <button
                    className={state.activeTab === tabs.search ? "nav active" : "nav"}
                    onClick={() => dispatch({ type: "SET_TAB", payload: tabs.search })}
                >
                    Search Documents
                </button>
            </aside>

            <main className="content">
                {state.activeTab === tabs.upload && (
                    <section>
                        <h3>Upload Document</h3>
                        <form onSubmit={onUpload}>
                            <input
                                type="file"
                                accept=".pdf,.txt"
                                onChange={(e) =>
                                    dispatch({ type: "SET_FILE", payload: e.target.files?.[0] ?? null })
                                }
                            />
                            <button type="submit" disabled={state.uploadBusy || !state.file}>
                                {state.uploadBusy ? "Uploading..." : "Upload"}
                            </button>
                        </form>
                        {state.uploadMessage && <p className="message">{state.uploadMessage}</p>}
                    </section>
                )}

                {state.activeTab === tabs.search && (
                    <section>
                        <h3>Search</h3>
                        <form onSubmit={onSearch}>
                            <textarea
                                rows={4}
                                placeholder="Ask a question from your indexed documents..."
                                value={state.question}
                                onChange={(e) => dispatch({ type: "SET_QUESTION", payload: e.target.value })}
                            />
                            <button type="submit" disabled={state.searchBusy || !state.question.trim()}>
                                {state.searchBusy ? "Searching..." : "Search"}
                            </button>
                        </form>

                        {state.answer && (
                            <div className="card">
                                <h4>Answer</h4>
                                <p>{state.answer}</p>
                            </div>
                        )}

                        {state.sources.length > 0 && (
                            <div className="card">
                                <h4>Sources</h4>
                                <ul>
                                    {state.sources.map((s, i) => (
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



// import React, { useState } from "react";
// import { searchDocuments, uploadDocument } from "./api";

// const tabs = {
//     upload: "upload",
//     search: "search"
// };

// export default function App() {
//     const [activeTab, setActiveTab] = useState(tabs.upload);

//     const [file, setFile] = useState(null);
//     const [uploadMessage, setUploadMessage] = useState("");
//     const [uploadBusy, setUploadBusy] = useState(false);

//     const [question, setQuestion] = useState("");
//     const [searchBusy, setSearchBusy] = useState(false);
//     const [answer, setAnswer] = useState("");
//     const [sources, setSources] = useState([]);

//     const onUpload = async (e) => {
//         e.preventDefault();
//         if (!file) return;

//         setUploadBusy(true);
//         setUploadMessage("");

//         try {
//             const result = await uploadDocument(file);
//             setUploadMessage(`Success: ${result.message}. Chunks: ${result.chunks}`);
//         } catch (err) {
//             setUploadMessage(`Upload failed: ${err.message}`);
//         } finally {
//             setUploadBusy(false);
//         }
//     };

//     const onSearch = async (e) => {
//         e.preventDefault();
//         if (!question.trim()) return;

//         setSearchBusy(true);
//         setAnswer("");
//         setSources([]);

//         try {
//             const result = await searchDocuments(question);
//             setAnswer(result.answer || "No answer returned.");
//             setSources(result.sources || []);
//         } catch (err) {
//             setAnswer(`Search failed: ${err.message}`);
//         } finally {
//             setSearchBusy(false);
//         }
//     };

//     return (
//         <div className="layout">
//             <aside className="sidebar">
//                 <h2>Lily RAG</h2>
//                 <button
//                     className={activeTab === tabs.upload ? "nav active" : "nav"}
//                     onClick={() => setActiveTab(tabs.upload)}
//                 >
//                     Upload Documents
//                 </button>
//                 <button
//                     className={activeTab === tabs.search ? "nav active" : "nav"}
//                     onClick={() => setActiveTab(tabs.search)}
//                 >
//                     Search Documents
//                 </button>
//             </aside>

//             <main className="content">
//                 {activeTab === tabs.upload && (
//                     <section>
//                         <h3>Upload Document</h3>
//                         <form onSubmit={onUpload}>
//                             <input
//                                 type="file"
//                                 accept=".pdf,.txt"
//                                 onChange={(e) => setFile(e.target.files?.[0] ?? null)}
//                             />
//                             <button type="submit" disabled={uploadBusy || !file}>
//                                 {uploadBusy ? "Uploading..." : "Upload"}
//                             </button>
//                         </form>
//                         {uploadMessage && <p className="message">{uploadMessage}</p>}
//                     </section>
//                 )}

//                 {activeTab === tabs.search && (
//                     <section>
//                         <h3>Search</h3>
//                         <form onSubmit={onSearch}>
//                             <textarea
//                                 rows={4}
//                                 placeholder="Ask a question from your indexed documents..."
//                                 value={question}
//                                 onChange={(e) => setQuestion(e.target.value)}
//                             />
//                             <button type="submit" disabled={searchBusy || !question.trim()}>
//                                 {searchBusy ? "Searching..." : "Search"}
//                             </button>
//                         </form>

//                         {answer && (
//                             <div className="card">
//                                 <h4>Answer</h4>
//                                 <p>{answer}</p>
//                             </div>
//                         )}

//                         {sources.length > 0 && (
//                             <div className="card">
//                                 <h4>Sources</h4>
//                                 <ul>
//                                     {sources.map((s, i) => (
//                                         <li key={i}>
//                                             <strong>Score:</strong> {Number(s.score).toFixed(3)}
//                                             <br />
//                                             {s.content}
//                                         </li>
//                                     ))}
//                                 </ul>
//                             </div>
//                         )}
//                     </section>
//                 )}
//             </main>
//         </div>
//     );
// }
