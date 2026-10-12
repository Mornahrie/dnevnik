using dnevnik.Entities;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;

namespace dnevnik.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<ParentStudent> ParentStudents => Set<ParentStudent>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<TeacherSubject> TeacherSubjects => Set<TeacherSubject>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<Attendance> Attendances => Set<Attendance>();
    public DbSet<Homework> Homeworks => Set<Homework>();
    public DbSet<HomeworkStatus> HomeworkStatuses => Set<HomeworkStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Login)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(u => u.IsActive)
            .HasDefaultValue(true);

        modelBuilder.Entity<User>()
            .Property(u => u.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        modelBuilder.Entity<HomeworkStatus>().ToTable("homework_status");
    }
}