class DebugSymbol < ApplicationRecord
  validates :path, presence: true, uniqueness: true
  validates :symbol_hash, presence: true
  
end
