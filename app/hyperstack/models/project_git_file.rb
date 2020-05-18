# frozen_string_literal: true

# A file in a LFS git project
class ProjectGitFile < ApplicationRecord
  belongs_to :lfs_project

  validates :name, presence: true, length: { maximum: 255, minimum: 1 }
  validates :path, presence: true, length: { maximum: 1024, minimum: 0 }
  validates :ftype, presence: true, inclusion: { in: %w[file folder] }

  scope :by_project, lambda { |project_id|
    where(lfs_project_id: project_id)
  }

  scope :with_path, lambda { |path|
    where(path: path)
  }

  scope :sort_by_name,
        server: -> { order('name ASC') },
        select: -> { sort_by(&:name) }

  scope :paginated, lambda { |off, count|
    offset(off).take(count)
  }

  def external?
    !lfs_oid.blank?
  end

  def lfs?
    !lfs_oid.blank?
  end

  def folder?
    ftype == 'folder'
  end

  def root?
    folder? && name == '/' && path == '/'
  end

  def size_readable
    if folder?
      size.to_s + ' item'.pluralize(size)
    else
      size_mib.to_s + ' MiB'
    end
  end

  def size_mib(rounded: 3)
    if size
      (size.to_f / 1024 / 1024).round(rounded)
    else
      0
    end
  end
end
