use sha2::{Digest, Sha256};
use std::collections::BTreeSet;
use std::fmt::Write;

pub const DEMO_ID_HASH_LEN: usize = 12;

pub fn sha256_hex(bytes: &[u8]) -> String {
    let digest = Sha256::digest(bytes);
    let mut out = String::with_capacity(64);
    for byte in digest {
        let _ = write!(&mut out, "{byte:02x}");
    }
    out
}

pub fn demo_id(stem: &str, demo_sha256: &str) -> String {
    format!(
        "{}-{}",
        slugify_demo_stem(stem),
        short_demo_hash(demo_sha256)
    )
}

pub fn unique_demo_id(base_id: &str, used_ids: &mut BTreeSet<String>) -> String {
    let mut id = base_id.to_string();
    let mut suffix = 2_u32;
    while !used_ids.insert(id.clone()) {
        id = format!("{base_id}-{suffix}");
        suffix += 1;
    }
    id
}

fn short_demo_hash(demo_sha256: &str) -> String {
    demo_sha256.chars().take(DEMO_ID_HASH_LEN).collect()
}

fn slugify_demo_stem(value: &str) -> String {
    let mut out = String::new();
    for ch in value.chars() {
        if ch.is_ascii_alphanumeric() || ch == '-' || ch == '_' {
            out.push(ch);
        } else if ch.is_whitespace() {
            out.push('_');
        }
    }
    if out.is_empty() {
        "demo".to_string()
    } else {
        out
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn content_hash_changes_demo_id_for_same_stem() {
        let first = demo_id("match", &sha256_hex(b"first demo"));
        let second = demo_id("match", &sha256_hex(b"second demo"));

        assert_ne!(first, second);
    }

    #[test]
    fn same_bytes_generate_same_demo_id_independent_of_path() {
        let hash = sha256_hex(b"same demo bytes");

        assert_eq!(demo_id("match", &hash), demo_id("match", &hash));
    }

    #[test]
    fn demo_id_slugifies_stem_and_uses_hash12() {
        let hash = sha256_hex(b"demo bytes");
        let id = demo_id("Spirit vs Falcons m2 Mirage!", &hash);

        assert_eq!(id, format!("Spirit_vs_Falcons_m2_Mirage-{}", &hash[..12]));
    }

    #[test]
    fn unique_demo_id_suffixes_duplicates() {
        let mut used = BTreeSet::new();

        assert_eq!(
            unique_demo_id("demo-abcdef123456", &mut used),
            "demo-abcdef123456"
        );
        assert_eq!(
            unique_demo_id("demo-abcdef123456", &mut used),
            "demo-abcdef123456-2"
        );
    }
}
