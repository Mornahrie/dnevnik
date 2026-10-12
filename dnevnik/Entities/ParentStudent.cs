using dnevnik.Entities;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dnevnik.Entities;

[Table("parent_student")]
public class ParentStudent
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("parent_id")]
    public int? ParentId { get; set; }
    public Parent? Parent { get; set; }

    [Column("student_id")]
    public int? StudentId { get; set; }
    public Student? Student { get; set; }
}