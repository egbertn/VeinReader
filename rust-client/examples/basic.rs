use chrono::NaiveDate;
use handlezer_rust_client::{HandlezerClient, RegisterHandRequest};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let client = HandlezerClient::builder()
        .base_url("http://localhost:5000")
        .api_key("hzr_example_key")
        .build();

    let health = client.health()?;
    println!("Health: {} / {}", health.status, health.service);

    let example_date = NaiveDate::from_ymd_opt(1995, 4, 12).unwrap();
    let register_result = client.register_hand(RegisterHandRequest {
        full_name: "Ada Lovelace".to_string(),
        birth_date: example_date,
        thumbprint_base64: "U0FNUExF".to_string(),
        photo_base64: None,
    })?;

    println!("Registered hand: {:?}", register_result.person_id);
    Ok(())
}
