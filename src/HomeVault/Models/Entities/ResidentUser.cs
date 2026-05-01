/*
 * FILE: ResidentUser.cs
 * PROJECT: HomeVault
 * FIRST VERSION: 2026-04-13
 * DESCRIPTION: Entity that stores a resident's login credentials. The
 *              password is BCrypt-hashed before being persisted.
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

        [Required, StringLength(200)]
        public string PasswordHash { get; set; } = null!;

        // Navigation property
        public Resident Resident { get; set; } = null!;
    }
}