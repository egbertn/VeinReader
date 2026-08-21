# Handlezer Rust client

A sync-first Rust client for interacting with the Handlezer REST API.

## Why sync-first?

This API is a service boundary, not a high-throughput streaming engine. A blocking client keeps the surface simpler, removes Tokio noise, and fits the design of a machine-to-machine integration very well.

## Features

- typed requests and responses
- support for API-key and bearer-token auth
- builder-based configuration
- ergonomic, Rust-flavored error handling
- no async runtime requirement for normal usage

## Quick start

```rust
use handlezer_rust_client::{HandlezerClient, RegisterHandRequest};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let client = HandlezerClient::builder()
        .base_url("http://localhost:5000")
        .api_key("hzr_...your-api-key...")
        .build();

    let health = client.health()?;
    println!("{} {}", health.status, health.service);

    let result = client.register_hand(RegisterHandRequest {
        full_name: "Ada Lovelace".to_string(),
        birth_date: chrono::NaiveDate::from_ymd_opt(1815, 12, 10).unwrap(),
        thumbprint_base64: "...base64...".to_string(),
        photo_base64: None,
    })?;

    println!("registered person: {:?}", result.person_id);
    Ok(())
}
```

## Auth modes

```rust
let api_key_client = HandlezerClient::with_api_key("http://localhost:5000", "hzr_...");
let bearer_client = HandlezerClient::with_bearer_token("http://localhost:5000", "jwt-token");
```

## API surface

The client includes methods for:

- `health()`
- `create_api_key()`
- `register_hand()`
- `recognize_hand()`
- `get_retention_settings()`
- `create_access_policy()`
- `evaluate_access_policy()`
- `create_distribution_policy()`
- `consume_distribution_policy()`
- `check_in_presence()`
- `check_out_presence()`

## Run the example

```bash
cd rust-client
cargo run --example basic
```
