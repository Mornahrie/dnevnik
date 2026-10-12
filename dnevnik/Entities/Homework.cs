using dnevnik.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("homework")]
public class Homework
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("subject_id")]
    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    [Column("class_id")]
    public int? ClassId { get; set; }
    public SchoolClass? Class { get; set; }

    [Column("teacher_id")]
    public int? TeacherId { get; set; }
    public Teacher? Teacher { get; set; }

    [Column("date_issued")]
    public DateTime DateIssued { get; set; }

    [Column("date_due")]
    public DateTime DateDue { get; set; }

    [Required]
    [Column("description")]
    public string Description { get; set; } = null!;

    public ICollection<HomeworkStatus> HomeworkStatuses { get; set; } = new List<HomeworkStatus>();
}