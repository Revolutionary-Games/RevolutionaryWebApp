class LauncherLink < ApplicationRecord
  belongs_to :user

  validates :link_code, presence: true, uniqueness: true
end
