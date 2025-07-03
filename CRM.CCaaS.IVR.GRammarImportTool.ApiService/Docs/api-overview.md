# API Overview

This document provides a comprehensive overview of the HTTP and SignalR endpoints exposed by the CRM.CCaaS.IVR.GRammarImportTool.ApiService.

---

## HTTP Endpoints

### 1. `GET /get-antiforgery-token`

- **Description:**  
  Retrieves an anti-forgery (CSRF) token for use in subsequent requests that require CSRF protection.
- **Request:**  
  No parameters required.
- **Response:**  
  - `200 OK`  
    ```json
    { "Token": "<anti-forgery-token>" }
    ```
  - The token is also returned in the `X-CSRF-TOKEN` response header.
- **Notes:**  
  Use this endpoint to obtain a CSRF token before making POST requests that require it.

---

### 2. `POST /grit`

- **Description:**  
  Accepts a ZIP file and a SignalR connection ID, processes the file in the background, and notifies the client of progress and completion via SignalR.
- **Request:**  
  - **Content-Type:** `multipart/form-data`
  - **Form Fields:**
    - `file` (IFormFile): The ZIP file to process. **Required.**
    - `connectionId` (string): The SignalR connection ID to receive progress and completion events. **Required.**
  - **Example (using JSON for reference, actual request must be multipart/form-data):**
    ```json
    {
      "file": "<base64-encoded-zip-file-contents>",
      "connectionId": "12345678-90ab-cdef-1234-567890abcdef"
    }
    ```
    > **Note:** The above is a conceptual example. The actual request must use `multipart/form-data` with a file upload and a string field.
- **Response:**  
  - `202 Accepted`  
    The request is accepted for background processing. Progress and results are sent via SignalR.
  - `400 Bad Request`  
    If the file is empty or the connectionId is missing.
- **Notes:**  
  - This endpoint disables anti-forgery protection.
  - The client must be connected to the SignalR hub and provide a valid connection ID.
- **Description:**  
  Accepts a ZIP file and a SignalR connection ID, processes the file in the background, and notifies the client of progress and completion via SignalR.
- **Request:**  
  - **Content-Type:** `multipart/form-data`
  - **Form Fields:**
    - `file` (IFormFile): The ZIP file to process. **Required.**
    - `connectionId` (string): The SignalR connection ID to receive progress and completion events. **Required.**
- **Response:**  
  - `202 Accepted`  
    The request is accepted for background processing. Progress and results are sent via SignalR.
  - `400 Bad Request`  
    If the file is empty or the connectionId is missing.
- **Notes:**  
  - This endpoint disables anti-forgery protection.
  - The client must be connected to the SignalR hub and provide a valid connection ID.

---

### 3. `GET /debug/dump-configuration`

- **Description:**  
  Triggers a dump of the current application configuration to the logs for debugging purposes.
- **Request:**  
  No parameters required.
- **Response:**  
  - `200 OK`  
    `"Configuration dumped to logs."`
- **Notes:**  
  Intended for debugging and should not be exposed in production environments.

---

### 4. `GET /health`

- **Description:**  
  Health check endpoint. Returns the health status of the application.
- **Request:**  
  No parameters required.
- **Response:**  
  - `200 OK`  
    Standard health check response.
- **Notes:**  
  Only available in test or development environments.

---

### 5. `GET /alive`

- **Description:**  
  Liveness probe endpoint. Returns if the application is alive.
- **Request:**  
  No parameters required.
- **Response:**  
  - `200 OK`  
    Standard liveness check response.
- **Notes:**  
  Only available in test or development environments.

---

## SignalR Hub Endpoints

### 1. `/gritHub` (SignalR Hub)

- **Description:**  
  Real-time communication hub for file processing progress and results.
- **Client Methods:**
  - `ConnectionId`  
    - **Arguments:** `string connectionId`  
    - **Description:** Sent to the client upon connection, providing the unique connection ID.
  - `Progress`  
    - **Arguments:** `int progress`, `string message`  
    - **Description:** Sent to the client to report progress updates during file processing.
  - `Completed`  
    - **Arguments:**  
      - For ZIP: `string resultBase64` (base64-encoded ZIP file)  
      - For /grit: `byte[] resultBytes` (raw ZIP file)  
    - **Description:** Sent to the client when processing is complete, containing the result.
  - `Error`  
    - **Arguments:** `string errorMessage`  
    - **Description:** Sent to the client if an error occurs during processing.

- **Server Methods (invokable by client):**
  - `GrxmlZipConvert(byte[] zipBytes)`  
    - **Description:** Uploads a ZIP file for processing. Progress and results are sent via the above client methods.
  - `GrxmlConvert(byte[] bytes)`  
    - **Description:** Uploads a single GRXML file for processing. Progress and results are sent via the above client methods.

---

## Summary Table

| Endpoint                       | Method | Description                                      | Auth/CSRF | Notes                        |
|------------------------------- |--------|--------------------------------------------------|-----------|------------------------------|
| `/get-antiforgery-token`       | GET    | Get anti-forgery token                           | None      | Returns token in header/body |
| `/grit`                        | POST   | Upload ZIP for background processing via SignalR  | No CSRF   | Requires SignalR connection  |
| `/debug/dump-configuration`    | GET    | Dump config to logs                              | None      | Debug only                   |
| `/health`                      | GET    | Health check                                     | None      | Dev/test only                |
| `/alive`                       | GET    | Liveness probe                                   | None      | Dev/test only                |
| `/gritHub` (SignalR)           | WS     | Real-time progress/results for file processing    | None      | SignalR hub                  |

---

## Additional Notes

- All endpoints are available under the main API service.
- The `/grit` endpoint is designed for asynchronous, large-file processing and requires the client to listen for SignalR events.
- The `/debug/dump-configuration` endpoint should be protected or disabled in production.
- Health and liveness endpoints are only mapped in test or development environments for security reasons.
