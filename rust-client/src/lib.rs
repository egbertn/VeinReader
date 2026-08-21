use std::time::Duration;

use chrono::{DateTime, NaiveDate, Utc};
use reqwest::blocking::Client;
use reqwest::StatusCode;
use serde::{de::DeserializeOwned, Deserialize, Serialize};
use thiserror::Error;

#[derive(Debug, Clone)]
pub enum Auth {
    ApiKey(String),
    Bearer(String),
    None,
}

#[derive(Debug, Clone)]
pub struct HandlezerClient {
    base_url: String,
    http: Client,
    auth: Auth,
}

#[derive(Debug, Clone)]
pub struct HandlezerClientBuilder {
    base_url: String,
    timeout: Duration,
    auth: Auth,
}

impl Default for HandlezerClientBuilder {
    fn default() -> Self {
        Self {
            base_url: "http://localhost:5000".to_string(),
            timeout: Duration::from_secs(15),
            auth: Auth::None,
        }
    }
}

impl HandlezerClientBuilder {
    pub fn base_url(mut self, url: impl Into<String>) -> Self {
        self.base_url = url.into();
        self
    }

    pub fn timeout(mut self, timeout: Duration) -> Self {
        self.timeout = timeout;
        self
    }

    pub fn api_key(mut self, api_key: impl Into<String>) -> Self {
        self.auth = Auth::ApiKey(api_key.into());
        self
    }

    pub fn bearer_token(mut self, token: impl Into<String>) -> Self {
        self.auth = Auth::Bearer(token.into());
        self
    }

    pub fn build(self) -> HandlezerClient {
        let http = Client::builder()
            .timeout(self.timeout)
            .build()
            .expect("reqwest blocking client should build");

        HandlezerClient {
            base_url: self.base_url.trim_end_matches('/').to_string(),
            http,
            auth: self.auth,
        }
    }
}

impl HandlezerClient {
    pub fn builder() -> HandlezerClientBuilder {
        HandlezerClientBuilder::default()
    }

    pub fn new(base_url: impl Into<String>) -> Self {
        Self::builder().base_url(base_url).build()
    }

    pub fn with_api_key(base_url: impl Into<String>, api_key: impl Into<String>) -> Self {
        Self::builder()
            .base_url(base_url)
            .api_key(api_key)
            .build()
    }

    pub fn with_bearer_token(base_url: impl Into<String>, token: impl Into<String>) -> Self {
        Self::builder()
            .base_url(base_url)
            .bearer_token(token)
            .build()
    }

    pub fn set_api_key(&mut self, api_key: impl Into<String>) {
        self.auth = Auth::ApiKey(api_key.into());
    }

    pub fn set_bearer_token(&mut self, token: impl Into<String>) {
        self.auth = Auth::Bearer(token.into());
    }

    pub fn clear_auth(&mut self) {
        self.auth = Auth::None;
    }

    pub fn health(&self) -> Result<HealthResponse, HandlezerError> {
        self.get("/health")
    }

    pub fn create_api_key(&self, request: CreateApiKeyRequest) -> Result<CreateApiKeyResponse, HandlezerError> {
        self.post("/api/auth/api-keys", Some(request))
    }

    pub fn register_hand(&self, request: RegisterHandRequest) -> Result<RegisterHandResult, HandlezerError> {
        self.post("/api/hands/register", Some(request))
    }

    pub fn recognize_hand(&self, request: RecognizeHandRequest) -> Result<RecognitionResult, HandlezerError> {
        self.post("/api/hands/recognize", Some(request))
    }

    pub fn get_retention_settings(&self) -> Result<DataRetentionOptions, HandlezerError> {
        self.get("/api/admin/retention")
    }

    pub fn create_access_policy(&self, request: AccessPolicyCreateRequest) -> Result<AccessPolicyResponse, HandlezerError> {
        self.post("/api/access-policies", Some(request))
    }

    pub fn evaluate_access_policy(
        &self,
        policy_id: uuid::Uuid,
        request: AccessPolicyEvaluateRequest,
    ) -> Result<AccessDecisionResponse, HandlezerError> {
        self.post(&format!("/api/access-policies/{policy_id}/evaluate"), Some(request))
    }

    pub fn create_distribution_policy(
        &self,
        request: DistributionPolicyCreateRequest,
    ) -> Result<DistributionPolicyResponse, HandlezerError> {
        self.post("/api/distribution-policies", Some(request))
    }

    pub fn consume_distribution_policy(
        &self,
        policy_id: uuid::Uuid,
        request: DistributionConsumeRequest,
    ) -> Result<DistributionConsumeResponse, HandlezerError> {
        self.post(&format!("/api/distribution-policies/{policy_id}/consume"), Some(request))
    }

    pub fn check_in_presence(&self, request: PresenceCheckInRequest) -> Result<PresenceResponse, HandlezerError> {
        self.post("/api/presence/check-in", Some(request))
    }

    pub fn check_out_presence(&self, request: PresenceCheckOutRequest) -> Result<PresenceResponse, HandlezerError> {
        self.post("/api/presence/check-out", Some(request))
    }

    fn get<T>(&self, path: &str) -> Result<T, HandlezerError>
    where
        T: DeserializeOwned,
    {
        self.request(path, None, reqwest::Method::GET)
    }

    fn post<T, B>(&self, path: &str, body: Option<B>) -> Result<T, HandlezerError>
    where
        T: DeserializeOwned,
        B: Serialize,
    {
        self.request(path, body, reqwest::Method::POST)
    }

    fn request<T, B>(&self, path: &str, body: Option<B>, method: reqwest::Method) -> Result<T, HandlezerError>
    where
        T: DeserializeOwned,
        B: Serialize,
    {
        let url = format!("{}{}", self.base_url.trim_end_matches('/'), path);
        let mut request = self.http.request(method, &url);

        match &self.auth {
            Auth::ApiKey(key) => {
                request = request.header("X-API-Key", key);
            }
            Auth::Bearer(token) => {
                request = request.header("Authorization", format!("Bearer {token}"));
            }
            Auth::None => {}
        }

        if let Some(body) = body {
            request = request.json(&body);
        }

        let response = request.send()?;
        let status = response.status();

        if status.is_success() {
            let text = response.text()?;
            if text.trim().is_empty() {
                return Err(HandlezerError::EmptyResponse);
            }

            let value = serde_json::from_str::<T>(&text)?;
            Ok(value)
        } else {
            let text = response.text()?;
            let message = if text.trim().is_empty() {
                format!("Request failed with status {status}.")
            } else {
                text
            };

            Err(HandlezerError::Api { status, message })
        }
    }
}

#[derive(Debug, Error)]
pub enum HandlezerError {
    #[error("http request failed: {0}")]
    Request(#[from] reqwest::Error),
    #[error("failed to deserialize API payload: {0}")]
    Json(#[from] serde_json::Error),
    #[error("API returned status {status}: {message}")]
    Api { status: StatusCode, message: String },
    #[error("API returned an empty response")]
    EmptyResponse,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CreateApiKeyRequest {
    pub name: String,
    pub scopes: Option<Vec<String>>,
    pub expires_at_utc: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CreateApiKeyResponse {
    pub id: uuid::Uuid,
    pub name: String,
    pub scopes: Vec<String>,
    pub expires_at_utc: Option<DateTime<Utc>>,
    pub api_key: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RegisterHandRequest {
    pub full_name: String,
    pub birth_date: NaiveDate,
    pub thumbprint_base64: String,
    pub photo_base64: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RecognizeHandRequest {
    pub thumbprint_base64: String,
    pub device_id: Option<String>,
    pub occurred_at_utc: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RegisterHandResult {
    pub person_id: uuid::Uuid,
    pub full_name: String,
    pub birth_date: NaiveDate,
    pub thumbprint_hash: String,
    pub photo_stored: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RecognitionResult {
    pub is_match: bool,
    pub person_id: Option<uuid::Uuid>,
    pub full_name: Option<String>,
    pub birth_date: Option<NaiveDate>,
    pub thumbprint_hash: String,
    pub occurred_at_utc: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HealthResponse {
    pub status: String,
    pub service: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DataRetentionOptions {
    pub audit_log_retention_days: i32,
    pub store_enrollment_photos: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AccessPolicyCreateRequest {
    pub name: String,
    pub rule_text: String,
    pub time_zone_id: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AccessPolicyResponse {
    pub id: uuid::Uuid,
    pub name: String,
    pub rule_text: String,
    pub time_zone_id: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AccessPolicyEvaluateRequest {
    pub person_id: Option<uuid::Uuid>,
    pub device_id: Option<String>,
    pub occurred_at_utc: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AccessDecisionResponse {
    pub allowed: bool,
    pub reason: String,
    pub evaluated_at_utc: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DistributionPolicyCreateRequest {
    pub name: String,
    pub daily_limit: i32,
    pub time_zone_id: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DistributionPolicyResponse {
    pub id: uuid::Uuid,
    pub name: String,
    pub daily_limit: i32,
    pub time_zone_id: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DistributionConsumeRequest {
    pub person_id: uuid::Uuid,
    pub device_id: Option<String>,
    pub occurred_at_utc: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DistributionConsumeResponse {
    pub allowed: bool,
    pub reason: String,
    pub remaining_today: i32,
    pub occurred_at_utc: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PresenceCheckInRequest {
    pub person_id: uuid::Uuid,
    pub device_id: Option<String>,
    pub occurred_at_utc: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PresenceCheckOutRequest {
    pub person_id: uuid::Uuid,
    pub device_id: Option<String>,
    pub occurred_at_utc: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PresenceResponse {
    pub successful: bool,
    pub reason: String,
    pub occurred_at_utc: DateTime<Utc>,
}

pub use uuid;
