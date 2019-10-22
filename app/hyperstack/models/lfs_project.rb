# frozen_string_literal: true

# An Git LFS project
class LfsProject < ApplicationRecord
  has_many :lfs_objects

  scope :visible_to, lambda { |id|
    user = User.find_by_id(id)
    user&.developer? ? all : where(public: true)
  }

  scope :order_by_name,
        server: lambda {
                  order('name DESC')
                },
        select: -> { sort { |a, b| b.name <=> a.name } }

  validates_uniqueness_of :slug
  validates_uniqueness_of :name

  validates :slug, presence: true, length: { maximum: 20, minimum: 3 }
  validates :slug, format: { with: /\A[A-Za-z0-9\-\_]+\z/ }

  validates :name, presence: true, length: { maximum: 100, minimum: 3 }
  validates :public, presence: true, inclusion: { in: [true, false] }
end
