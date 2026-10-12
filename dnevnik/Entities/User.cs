using dnevnik.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("users")]
public class User
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    [Column("login")]
    public string Login { get; set; } = null!;

    [Required, MaxLength(255)]
    [Column("password_hash")]
    public string PasswordHash { get; set; } = null!;

    [Column("role_id")]
    public int? RoleId { get; set; }
    public Role? Role { get; set; }

    [Required, MaxLength(200)]
    [Column("full_name")]
    public string FullName { get; set; } = null!;

    [MaxLength(100)]
    [Column("email")]
    public string? Email { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Student? Student { get; set; }
    public Parent? Parent { get; set; }
    public Teacher? Teacher { get; set; }
}