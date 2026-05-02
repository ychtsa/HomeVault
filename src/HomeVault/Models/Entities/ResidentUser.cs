/*
 * FILE: ResidentUser.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-13
 * DESCRIPTION: Entity that stores a resident's login credentials. The
 *              password is BCrypt-hashed before being persisted. Email is
 *              unique and used for password recovery; the recovery flow
 *              persists only a SHA-256 hash of the issued reset token plus
 *              its expiry timestamp, so a leaked database cannot be used
 *              to reset arbitrary user passwords.
 */

using System.ComponentModel.DataAnnotations;

namespace HomeVault.Models.Entities
{
    public class ResidentUser
    {
        // ResidentId is both PK and FK to Resident (1-to-1).
        [Key]
        [StringLength(5)]
        public string ResidentId { get; set; } = null!;

        [Required, StringLength(30)]
        public string Username { get; set; } = null!;

        [Required, StringLength(100), EmailAddress]
        public string Email { get; set; } = null!;

        [Required, StringLength(200)]
        public string PasswordHash { get; set; } = null!;

        // SHA-256 hex of the active password-reset token (64 chars), or null
        // if no reset is in flight. Storing only the hash means a leaked
        // database cannot be used to reset passwords — an attacker would
        // need the original token from the user's inbox.
        [StringLength(64)]
        public string? PasswordResetTokenHash { get; set; }

        // UTC expiry of the active reset token. Tokens older than this are
        // ignored even if the hash matches.
        public DateTime? PasswordResetTokenExpiresAt { get; set; }

        // Navigation property
        public Resident Resident { get; set; } = null!;
    }
}
