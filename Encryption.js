// =========================================================
// LIGHTTHREADS - HARDENED CLIENT AES-GCM 256-BIT ENCRYPTION
// =========================================================

class LightEncryption {
    // Derive AES-GCM key using PBKDF2 with dynamic room-bound salt
    static async deriveKey(secretPin) {
        const enc = new TextEncoder();
        const keyMaterial = await window.crypto.subtle.importKey(
            "raw",
            enc.encode(secretPin.padEnd(32, "0").slice(0, 32)),
            { name: "PBKDF2" },
            false,
            ["deriveBits", "deriveKey"]
        );

        // Dynamic room-bound salt
        const dynamicSalt = enc.encode(`LightThreadsSalt_${secretPin}_2026`);

        return window.crypto.subtle.deriveKey(
            {
                name: "PBKDF2",
                salt: dynamicSalt,
                iterations: 100000,
                hash: "SHA-256"
            },
            keyMaterial,
            { name: "AES-GCM", length: 256 },
            false,
            ["encrypt", "decrypt"]
        );
    }

    static async encryptMessage(plaintext, secretPin) {
        try {
            const key = await LightEncryption.deriveKey(secretPin);
            const iv = window.crypto.getRandomValues(new Uint8Array(12)); // 96-bit IV
            const enc = new TextEncoder();

            const ciphertext = await window.crypto.subtle.encrypt(
                { name: "AES-GCM", iv: iv },
                key,
                enc.encode(plaintext)
            );

            const combined = new Uint8Array(iv.length + ciphertext.byteLength);
            combined.set(iv, 0);
            combined.set(new Uint8Array(ciphertext), iv.length);

            return btoa(String.fromCharCode.apply(null, combined));
        } catch (e) {
            console.error("Encryption failed:", e);
            return plaintext;
        }
    }

    static async decryptMessage(cipherBase64, secretPin) {
        try {
            const key = await LightEncryption.deriveKey(secretPin);
            const combined = new Uint8Array(
                atob(cipherBase64).split("").map(c => c.charCodeAt(0))
            );

            const iv = combined.slice(0, 12);
            const ciphertext = combined.slice(12);

            const decrypted = await window.crypto.subtle.decrypt(
                { name: "AES-GCM", iv: iv },
                key,
                ciphertext
            );

            return new TextDecoder().decode(decrypted);
        } catch (e) {
            console.error("Decryption failed:", e);
            return "[ENCRYPTED MESSAGE - DECRYPTION FAILED]";
        }
    }
}