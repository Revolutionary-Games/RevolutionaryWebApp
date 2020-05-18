# frozen_string_literal: true

# An Git LFS project
class LfsProject < ApplicationRecord
  has_many :lfs_objects
  has_many :project_git_files

  scope :visible_to, lambda { |id|
    user = User.find_by_id(id)
    user&.developer? ? all : where(public: true)
  }

  scope :order_by_name,
        server: lambda {
                  order('name ASC')
                },
        select: -> { sort_by(&:name) }

  scope :paginated, lambda { |off, count|
    offset(off).take(count)
  }

  validates_uniqueness_of :slug
  validates_uniqueness_of :name

  validates :slug, presence: true, length: { maximum: 20, minimum: 3 }
  validates :slug, format: { with: /\A[A-Za-z0-9\-\_]+\z/ }

  validates :name, presence: true, length: { maximum: 100, minimum: 3 }
  validates :public, inclusion: { in: [true, false] }

  def total_size_mib(rounded: 2)
    if total_object_size
      (total_object_size.to_f / 1024 / 1024).round(rounded)
    else
      0
    end
  end

  server_method :lfs_url, default: '' do
    if ENV['BASE_URL']
      URI.join(ENV['BASE_URL'], "/api/v1/lfs/#{slug}").to_s
    else
      'NO BASE URL'
    end
  end
end
