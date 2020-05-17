# frozen_string_literal: true

# A Git LFS object belonging to a project
class LfsObject < ApplicationRecord
  belongs_to :lfs_project

  validates :oid, presence: true, length: { minimum: 5 }
  validates :storage_path, presence: true, length: { minimum: 3 }
  validates :size, presence: true, numericality: { only_integer: true }

  scope :by_project, lambda { |project_id|
    where(lfs_project_id: project_id)
  }

  scope :sort_by_created_at,
        server: -> { order('created_at DESC') },
        select: -> { sort { |a, b| b.created_at <=> a.created_at } }

  scope :paginated, lambda { |off, count|
    offset(off).take(count)
  }

  def size_mib(rounded: 2)
    if size
      (size.to_f / 1024 / 1024).round(rounded)
    else
      0
    end
  end
end
