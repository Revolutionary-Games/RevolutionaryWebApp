# frozen_string_literal: true

# A Git LFS object belonging to a project
class LfsObject < ApplicationRecord
  belongs_to :lfs_project

  validates :hash, presence: true, length: { minimum: 5 }
  validates :storage_path, presence: true, length: { minimum: 3 }
  validates :size, presence: true, numericality: { only_integer: true }
end
